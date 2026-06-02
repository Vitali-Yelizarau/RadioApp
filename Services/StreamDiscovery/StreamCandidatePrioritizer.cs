using RadioApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RadioApp.Services.StreamDiscovery
{
    internal class StreamCandidatePrioritizer
    {
        private readonly StreamCandidateFilter _filter;
        public StreamCandidatePrioritizer()
        {
            _filter = new StreamCandidateFilter();
        }

        public int GetStreamQualityScore(DiscoveredRadioStream streamInfo)
        {
            if (streamInfo == null)
            {
                return 0;
            }

            if (streamInfo.Bitrate.HasValue)
            {
                return streamInfo.Bitrate.Value;
            }

            return 96;
        }
        public StreamCandidateEvaluation EvaluateStreamCandidateUrl(string url)
        {
            var result = new StreamCandidateEvaluation
            {
                Url = url,
                Score = 0,
                Reason = string.Empty
            };

            if (string.IsNullOrWhiteSpace(url))
            {
                return result;
            }

            if (_filter.IsDefinitelyNotStreamUrl(url) || _filter.IsRejectedStreamCandidate(url))
            {
                result.Score = -1000;
                result.Reason = "Rejected by non-stream filter";
                return result;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                result.Score = -1000;
                result.Reason = "Invalid absolute URL";
                return result;
            }

            string lower = url.ToLowerInvariant();
            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath.ToLowerInvariant();

            int bitrate = TryExtractBitrateFromUrl(url);

            if (bitrate > 0)
            {
                result.Bitrate = bitrate;
                result.Score += bitrate;
                result.Reason += "bitrate-from-url;";
            }

            if (lower.Contains("_hd") ||
                lower.Contains("-hd") ||
                lower.Contains("/hd") ||
                lower.Contains("hd/"))
            {
                result.Score += 80;
                result.Reason += "hd-hint;";
            }

            if (host.EndsWith("stationplaylist.com", StringComparison.OrdinalIgnoreCase) &&
            (
                !uri.IsDefaultPort ||
                path.Contains("/stream") ||
                path.Contains("/;") ||
                path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Length >= 1
            ))
            {
                result.Score += 850;
                result.Reason += "stationplaylist-icecast-endpoint;";
            }

            if (host.Equals("stream.rcast.net", StringComparison.OrdinalIgnoreCase) ||
                (
                    host.Equals("players.rcast.net", StringComparison.OrdinalIgnoreCase) &&
                    path.StartsWith("/stream/")
                ))
            {
                result.Score += 750;
                result.Reason += "rcast-direct-stream;";
            }

            bool isMyRadioStreamShoutcastEndpoint =
                    host.EndsWith(".myradiostream.com") &&
                    (
                        !uri.IsDefaultPort ||
                        path.Contains("/;") ||
                        Regex.IsMatch(path, @"^/\d+/?;")
                    );

            if (isMyRadioStreamShoutcastEndpoint)
            {
                result.Score += 900;
                result.Reason += "myradiostream-shoutcast-endpoint;";
            }

            if (host.Contains("myradiostream.com") &&
                path.Contains("/embed/"))
            {
                result.Score -= 300;
                result.Reason += "myradiostream-embed-page-penalty;";
            }

            // Deutschland.fm / Antenne Bayern real stream hosts.
            // ВАЖНО: dist=deutschlandfm сам по себе НЕ является признаком stream URL.
            if (lower.Contains("mp3channels.webradio.") ||
                host.Contains("webradio.antenne.de"))
            {
                result.Score += 800;
                result.Reason += "deutschland-fm-webradio-stream;";
            }

            // Stable entry point is better than temporary redirected server like s4-webradio.
            if (lower.Contains("mp3channels.webradio."))
            {
                result.Score += 250;
                result.Reason += "deutschland-fm-stable-entrypoint;";
            }

            // Penalize fake relative candidates incorrectly normalized to deutschland.fm host.
            if ((host.Equals("www.deutschland.fm", StringComparison.OrdinalIgnoreCase) ||
                 host.Equals("deutschland.fm", StringComparison.OrdinalIgnoreCase)) &&
                (path.Contains("mp3") ||
                 path.Contains("stream") ||
                 path.Contains("icecast") ||
                 path.Contains("/listen") ||
                 path.StartsWith("/radio/")))
            {
                result.Score -= 1000;
                result.Reason += "deutschland-fm-fake-relative-stream;";
            }

            if (lower.Contains(".mp3") || path.Contains("/mp3-") || path.Contains("mp3-"))
            {
                result.Score += 140;
                result.Reason += "mp3;";
            }

            if (lower.Contains(".aac") || path.Contains("/aac-") || path.Contains("aac-"))
            {
                result.Score += 120;
                result.Reason += "aac;";
            }

            if (lower.Contains(".ogg") || lower.Contains(".opus"))
            {
                result.Score += 110;
                result.Reason += "ogg-opus;";
            }

            if (path.Contains("/stream") || path.Contains("/streams"))
            {
                result.Score += 100;
                result.Reason += "stream-path;";
            }

            if (path.Contains("/live"))
            {
                result.Score += 90;
                result.Reason += "live-path;";
            }

            if (path.Contains("/listen"))
            {
                result.Score += 80;
                result.Reason += "listen-path;";
            }

            if (path.Contains("/radio"))
            {
                result.Score += 70;
                result.Reason += "radio-path;";
            }

            if (host.StartsWith("online."))
            {
                result.Score += 80;
                result.Reason += "online-host;";
            }

            if (host.StartsWith("listen."))
            {
                result.Score += 120;
                result.Reason += "listen-host;";
            }

            if (host.StartsWith("stream.") || host.StartsWith("streams."))
            {
                result.Score += 100;
                result.Reason += "stream-host;";
            }

            if (host.Contains(".stream.") || host.Contains(".streams."))
            {
                result.Score += 80;
                result.Reason += "stream-host-part;";
            }

            if (host.Contains("ice") || lower.Contains("icecast") || lower.Contains("shoutcast"))
            {
                result.Score += 70;
                result.Reason += "icecast-like;";
            }

            if (lower.Contains("/manifest/"))
            {
                result.Score -= 200;
                result.Reason += "manifest-penalty;";
            }

            return result;
        }
        public int TryExtractBitrateFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return 0;
            }

            var match = Regex.Match(
                url,
                @"(?:mp3|aac|opus|ogg)[-_](?<bitrate>\d{2,3})",
                RegexOptions.IgnoreCase
            );

            if (!match.Success)
            {
                return 0;
            }

            if (int.TryParse(match.Groups["bitrate"].Value, out int bitrate))
            {
                return bitrate;
            }

            return 0;
        }
        public int GetCandidatePriority(string url)
        {
            string lower = url.ToLowerInvariant();

            if (lower.Contains("securenetsystems.net/v5/"))
                return 1000;

            if (lower.Contains("amperwave") || lower.Contains("/manifest"))
                return 900;

            if (lower.Contains(".m3u8") || lower.Contains(".pls") || lower.Contains(".m3u"))
                return 800;

            if (lower.Contains(".mp3") || lower.Contains(".aac") || lower.Contains(".ogg") || lower.Contains(".opus"))
                return 700;

            if (lower.Contains("icecast") || lower.Contains("shoutcast") || lower.Contains("streamguys") || lower.Contains("tritondigital"))
                return 600;

            if (lower.Contains("/stream") || lower.Contains("/live") || lower.Contains("/listen"))
                return 500;

            return 0;
        }
        public List<string> SortCandidatesByPriority(List<string> candidates)
        {
            return candidates
                .OrderByDescending(GetCandidatePriority)
                .ThenBy(x => x.Length)
                .ToList();
        }
    }
}
