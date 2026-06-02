using RadioApp.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RadioApp.Services.StreamDiscovery
{
    public class GenericStreamDiscoveryService
    {
        private readonly RadioStreamInfoService _streamInfoService;
        private readonly StreamCandidateFilter _streamCandidateFilter;
        private readonly StreamCandidatePrioritizer _streamCandidatePrioritizer;
        public GenericStreamDiscoveryService()
        {
            _streamInfoService = new RadioStreamInfoService();
            _streamCandidateFilter = new StreamCandidateFilter();
            _streamCandidatePrioritizer = new StreamCandidatePrioritizer();
        }
        public string ToDisplayName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string text = raw
                .Trim()
                .Replace("-", " ")
                .Replace("_", " ");

            text = Regex.Replace(text, @"\s+", " ");

            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text);
        }
        public string ExtractNameFromStreamUrl(string streamUrl)
        {
            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                return string.Empty;
            }

            if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out Uri uri))
            {
                return string.Empty;
            }

            string[] parts = uri.AbsolutePath
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                return string.Empty;
            }

            foreach (string part in parts)
            {
                string lower = part.ToLowerInvariant();

                if (lower.Contains("mp3") ||
                    lower.Contains("aac") ||
                    lower.Contains("stream") ||
                    lower.Contains("radio") ||
                    lower.Contains("live") ||
                    lower.Contains("radiode"))
                {
                    continue;
                }

                return ToDisplayName(part);
            }

            return ToDisplayName(parts[0]);
        }
        public string ExtractNameFromPageUrl(string pageUrl)
        {
            if (string.IsNullOrWhiteSpace(pageUrl))
            {
                return string.Empty;
            }

            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri uri))
            {
                return string.Empty;
            }

            string lastSegment = uri.Segments
                .Select(x => x.Trim('/'))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .LastOrDefault();

            if (string.IsNullOrWhiteSpace(lastSegment))
            {
                return string.Empty;
            }

            return ToDisplayName(lastSegment);
        }
        public string BuildFallbackStationName(string pageUrl, string streamUrl)
        {
            string nameFromPageUrl = ExtractNameFromPageUrl(pageUrl);

            if (!string.IsNullOrWhiteSpace(nameFromPageUrl))
            {
                return nameFromPageUrl;
            }

            string nameFromStreamUrl = ExtractNameFromStreamUrl(streamUrl);

            if (!string.IsNullOrWhiteSpace(nameFromStreamUrl))
            {
                return nameFromStreamUrl;
            }

            return "New radio station";
        }
        public string CleanPageTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            string cleaned = title.Trim();

            cleaned = cleaned.Replace("&amp;", "&");
            cleaned = cleaned.Replace("&#x27;", "'");
            cleaned = cleaned.Replace("&quot;", "\"");

            cleaned = cleaned.Replace(" | Listen Online", "");
            cleaned = cleaned.Replace(" | radio.net", "");
            cleaned = cleaned.Replace(" - radio.net", "");

            return cleaned.Trim();
        }
        public string ExtractPageTitle(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var ogTitleMatch = Regex.Match(
                html,
                @"<meta[^>]+property=[""']og:title[""'][^>]+content=[""'](?<title>[^""']+)[""']",
                RegexOptions.IgnoreCase
            );

            if (ogTitleMatch.Success)
            {
                return CleanPageTitle(ogTitleMatch.Groups["title"].Value);
            }

            var titleMatch = Regex.Match(
                html,
                @"<title[^>]*>(?<title>.*?)</title>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );

            if (titleMatch.Success)
            {
                return CleanPageTitle(titleMatch.Groups["title"].Value);
            }

            return string.Empty;
        }
        public string BuildTopCandidateDescription(List<StreamCandidateEvaluation> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return string.Empty;
            }

            var top = candidates
                .Where(x => x != null)
                .Where(x => x.Score > 0)
                .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .Where(x => !_streamCandidateFilter.IsDefinitelyNotStreamUrl(x.Url))
                .Where(x => !_streamCandidateFilter.IsRejectedStreamCandidate(x.Url))
                .Where(x => !_streamCandidateFilter.IsRejectedFinalStreamUrl(x.Url))
                .Where(x =>
                    x.Url.ToLowerInvariant().Contains(".mp3") ||
                    x.Url.ToLowerInvariant().Contains(".aac") ||
                    x.Url.ToLowerInvariant().Contains(".ogg") ||
                    x.Url.ToLowerInvariant().Contains(".opus") ||
                    x.Url.ToLowerInvariant().Contains("/stream") ||
                    x.Url.ToLowerInvariant().Contains("/live") ||
                    x.Url.ToLowerInvariant().Contains("stream.") ||
                    x.Url.ToLowerInvariant().Contains(".stream.") ||
                    x.Url.ToLowerInvariant().Contains("streams."))
                .OrderByDescending(x => x.Score)
                .Take(3)
                .Select((x, index) =>
                    (index + 1) + ". " +
                    (x.Bitrate.HasValue ? x.Bitrate.Value + " kbps" : "unknown bitrate") +
                    " | score " + x.Score +
                    " | " + x.Url
                )
                .ToList();

            if (top.Count == 0)
            {
                return string.Empty;
            }

            return "Detected stream candidates:\r\n" + string.Join("\r\n", top);
        }
        public async Task<DiscoveredRadioStream> TryDiscoverBestGenericCandidateAsync(
                                                            string pageUrl,
                                                            List<string> candidates,
                                                            string html)
        {
            var evaluatedCandidates = candidates
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(x => !_streamCandidateFilter.IsRejectedStreamCandidate(x))
                .Where(x => !_streamCandidateFilter.IsRejectedFinalStreamUrl(x))
                .Where(x => !_streamCandidateFilter.IsDefinitelyNotStreamUrl(x))
                .Select(_streamCandidatePrioritizer.EvaluateStreamCandidateUrl)
                .Where(x => x.Score > 0)
                .ToList();

            ApplyPageContextPriority(pageUrl, evaluatedCandidates);

            evaluatedCandidates = evaluatedCandidates
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ToList();

            Log.Information(
                "Generic candidate evaluation produced {Count} candidates.",
                evaluatedCandidates.Count
            );

            foreach (var item in evaluatedCandidates.Take(10))
            {
                Log.Information(
                    "Generic candidate. Score: {Score}, Url: {Url}, Reason: {Reason}, Bitrate: {Bitrate}",
                    item.Score,
                    item.Url,
                    item.Reason,
                    item.Bitrate.HasValue ? item.Bitrate.Value.ToString() : "unknown"
                );
            }

            foreach (var candidate in evaluatedCandidates)
            {
                Log.Debug(
                    "Evaluating stream candidate. Score: {Score}, Url: {Url}, Reason: {Reason}",
                    candidate.Score,
                    candidate.Url,
                    candidate.Reason
                );

                try
                {
                    DiscoveredRadioStream streamInfo =
                        await _streamInfoService.GetStreamInfoIfPlayableAsync(pageUrl, candidate.Url);

                    if (streamInfo == null)
                    {
                        continue;
                    }

                    if (_streamCandidateFilter.IsRejectedStreamCandidate(streamInfo.StreamUrl) ||
                        _streamCandidateFilter.IsRejectedFinalStreamUrl(streamInfo.StreamUrl) ||
                        _streamCandidateFilter.IsDefinitelyNotStreamUrl(streamInfo.StreamUrl))
                    {
                        Log.Debug(
                            "HTTP-confirmed candidate was rejected after final URL check. Candidate: {Candidate}, FinalUrl: {FinalUrl}",
                            candidate.Url,
                            streamInfo.StreamUrl
                        );

                        continue;
                    }

                    candidate.IsHttpConfirmed = true;
                    candidate.StreamInfo = streamInfo;
                    candidate.Score += 500;

                    if (streamInfo.Bitrate.HasValue)
                    {
                        candidate.Score += streamInfo.Bitrate.Value;
                    }

                    Log.Information(
                        "HTTP-confirmed stream candidate. Score: {Score}, CandidateUrl: {CandidateUrl}, FinalUrl: {FinalUrl}, Bitrate: {Bitrate}",
                        candidate.Score,
                        candidate.Url,
                        streamInfo.StreamUrl,
                        streamInfo.Bitrate.HasValue ? streamInfo.Bitrate.Value.ToString() : "unknown"
                    );

                    if (candidate.Score >= 1000)
                    {
                        DiscoveredRadioStream earlyResult = BuildDiscoveredStreamFromConfirmedCandidate(
                                                                    pageUrl,
                                                                    html,
                                                                    candidate,
                                                                    evaluatedCandidates
                                                                );

                        Log.Information(
                            "Strong HTTP-confirmed generic stream selected early. SavedStreamUrl: {SavedStreamUrl}, FinalCheckedUrl: {FinalCheckedUrl}, StationName: {StationName}, Score: {Score}",
                            earlyResult.StreamUrl,
                            streamInfo.StreamUrl,
                            earlyResult.StationName,
                            candidate.Score
                        );

                        return earlyResult;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "HTTP check failed for candidate: {Url}", candidate.Url);
                }
            }

            var confirmed = evaluatedCandidates
                .Where(x => x.StreamInfo != null)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (confirmed != null)
            {
                DiscoveredRadioStream streamInfo = confirmed.StreamInfo;

                string pageTitle = ExtractPageTitle(html);

                string stationName = streamInfo.StationName;

                if (!string.IsNullOrWhiteSpace(pageTitle))
                {
                    stationName = pageTitle;
                }

                if (string.IsNullOrWhiteSpace(stationName))
                {
                    stationName = BuildFallbackStationName(pageUrl, confirmed.Url);
                }

                string description = streamInfo.Description;

                if (string.IsNullOrWhiteSpace(description))
                {
                    description = BuildTopCandidateDescription(evaluatedCandidates);
                }

                var result = new DiscoveredRadioStream
                {
                    PageUrl = pageUrl,
                    StreamUrl = confirmed.Url,
                    StationName = stationName,
                    Description = description,
                    Genre = streamInfo.Genre,
                    Bitrate = streamInfo.Bitrate
                };

                Log.Information(
                    "Best HTTP-confirmed generic stream selected. SavedStreamUrl: {SavedStreamUrl}, FinalCheckedUrl: {FinalCheckedUrl}, StationName: {StationName}",
                    result.StreamUrl,
                    streamInfo.StreamUrl,
                    result.StationName
                );

                return result;
            }

            var strongUnconfirmed = evaluatedCandidates
                .Where(x => x.Score >= 250 && x.Bitrate.HasValue)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (strongUnconfirmed != null)
            {
                string pageTitle = ExtractPageTitle(html);

                string stationName = !string.IsNullOrWhiteSpace(pageTitle)
                    ? pageTitle
                    : BuildFallbackStationName(pageUrl, strongUnconfirmed.Url);

                Log.Warning(
                    "Using strong unconfirmed stream candidate. Score: {Score}, Url: {Url}, Reason: {Reason}",
                    strongUnconfirmed.Score,
                    strongUnconfirmed.Url,
                    strongUnconfirmed.Reason
                );

                return new DiscoveredRadioStream
                {
                    PageUrl = pageUrl,
                    StreamUrl = strongUnconfirmed.Url,
                    StationName = stationName,
                    Description = BuildTopCandidateDescription(evaluatedCandidates),
                    Genre = string.Empty,
                    Bitrate = strongUnconfirmed.Bitrate
                };
            }

            Log.Information("No suitable generic stream candidate found.");

            return null;
        }

        private string BuildAlternativeCandidatesDescription(
    StreamCandidateEvaluation selectedCandidate,
    List<StreamCandidateEvaluation> evaluatedCandidates)
        {
            if (evaluatedCandidates == null || evaluatedCandidates.Count == 0)
            {
                return string.Empty;
            }

            string selectedUrl = selectedCandidate != null
                ? selectedCandidate.Url
                : string.Empty;

            List<StreamCandidateEvaluation> alternatives = evaluatedCandidates
                .Where(x => x != null)
                .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .Where(x => !x.Url.Equals(selectedUrl, StringComparison.OrdinalIgnoreCase))
                .Where(x => x.Score > 0)
                .Where(x => !_streamCandidateFilter.IsDefinitelyNotStreamUrl(x.Url))
                .Where(x => !_streamCandidateFilter.IsRejectedStreamCandidate(x.Url))
                .Where(x => !_streamCandidateFilter.IsRejectedFinalStreamUrl(x.Url))
                .Where(x => LooksLikeRealStreamForDescription(x.Url))
                .OrderByDescending(x => IsHdCandidate(x.Url))
                .ThenByDescending(x => x.Score)
                .Take(5)
                .ToList();

            if (alternatives.Count == 0)
            {
                return string.Empty;
            }

            var lines = new List<string>();

            lines.Add("Alternative stream candidates:");

            for (int i = 0; i < alternatives.Count; i++)
            {
                StreamCandidateEvaluation candidate = alternatives[i];

                string qualityLabel = IsHdCandidate(candidate.Url)
                    ? "HD"
                    : "standard";

                lines.Add(
                    (i + 1) + ". " +
                    qualityLabel +
                    " | " +
                    (candidate.Bitrate.HasValue ? candidate.Bitrate.Value + " kbps" : "unknown bitrate") +
                    " | score " + candidate.Score +
                    " | " + candidate.Url
                );
            }

            return string.Join("\r\n", lines);
        }

        private bool LooksLikeRealStreamForDescription(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string lower = url.ToLowerInvariant();

            return lower.Contains(".mp3")
                || lower.Contains(".aac")
                || lower.Contains(".ogg")
                || lower.Contains(".opus")
                || lower.Contains("/stream")
                || lower.Contains("/live")
                || lower.Contains("/listen")
                || lower.Contains("stream.")
                || lower.Contains("streams.")
                || lower.Contains(".stream.")
                || lower.Contains(".streams.")
                || lower.Contains("online.")
                || lower.Contains("tvstitch")
                || lower.Contains("radioroks");
        }

        private bool IsHdCandidate(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string lower = url.ToLowerInvariant();

            return lower.Contains("_hd")
                || lower.Contains("-hd")
                || lower.Contains("/hd")
                || lower.Contains("hd/");
        }

        private void ApplyPageContextPriority(
                                string pageUrl,
                                List<StreamCandidateEvaluation> candidates)
        {
            if (string.IsNullOrWhiteSpace(pageUrl) || candidates == null || candidates.Count == 0)
            {
                return;
            }

            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri pageUri))
            {
                return;
            }

            string pagePath = pageUri.AbsolutePath.ToLowerInvariant();

            string stationSlug = pageUri.Segments
                .Select(x => x.Trim('/'))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .LastOrDefault();

            if (string.IsNullOrWhiteSpace(stationSlug))
            {
                return;
            }

            stationSlug = stationSlug.ToLowerInvariant();

            foreach (var candidate in candidates)
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.Url))
                {
                    continue;
                }

                string candidateLower = candidate.Url.ToLowerInvariant();

                if (candidateLower.Contains(stationSlug))
                {
                    candidate.Score += 700;
                    candidate.Reason += "page-slug-match;";
                }

                if (stationSlug == "oldies")
                {
                    if (candidateLower.Contains("oldie") ||
                        candidateLower.Contains("oldies") ||
                        candidateLower.Contains("oldieclassics"))
                    {
                        candidate.Score += 900;
                        candidate.Reason += "absolut-oldies-match;";
                    }

                    if (candidateLower.Contains("absolut-ai") ||
                        candidateLower.Contains("absolut-rock") ||
                        candidateLower.Contains("absolut-80er") ||
                        candidateLower.Contains("absolut-hot") ||
                        candidateLower.Contains("absolut-top") ||
                        candidateLower.Contains("absolut-relax") ||
                        candidateLower.Contains("absolut-bella") ||
                        candidateLower.Contains("absolut-germany") ||
                        candidateLower.Contains("absolut-musicxl") ||
                        candidateLower.Contains("absolut-coffeemusic"))
                    {
                        candidate.Score -= 500;
                        candidate.Reason += "absolut-other-station-penalty;";
                    }
                }

                if (pagePath.Contains("/sender/") &&
                    candidateLower.Contains("live-sm.absolutradio.de"))
                {
                    candidate.Score += 150;
                    candidate.Reason += "absolut-live-sm-stream;";
                }
            }
        }

        private DiscoveredRadioStream BuildDiscoveredStreamFromConfirmedCandidate(
                                        string pageUrl,
                                        string html,
                                        StreamCandidateEvaluation confirmed,
                                        List<StreamCandidateEvaluation> evaluatedCandidates)
        {
            DiscoveredRadioStream streamInfo = confirmed.StreamInfo;

            string pageTitle = ExtractPageTitle(html);

            string stationName = streamInfo.StationName;

            if (!string.IsNullOrWhiteSpace(pageTitle))
            {
                stationName = pageTitle;
            }

            if (string.IsNullOrWhiteSpace(stationName))
            {
                stationName = BuildFallbackStationName(pageUrl, confirmed.Url);
            }

            string description = streamInfo.Description;

            string alternativeDescription = BuildAlternativeCandidatesDescription(
                confirmed,
                evaluatedCandidates
            );

            if (string.IsNullOrWhiteSpace(description))
            {
                description =
                    "Detected stream candidate:\r\n" +
                    "1. " +
                    (confirmed.Bitrate.HasValue ? confirmed.Bitrate.Value + " kbps" : "unknown bitrate") +
                    " | score " + confirmed.Score +
                    " | " + confirmed.Url;
            }

            if (!string.IsNullOrWhiteSpace(alternativeDescription))
            {
                description += "\r\n\r\n" + alternativeDescription;
            }

            return new DiscoveredRadioStream
            {
                PageUrl = pageUrl,

                StreamUrl = confirmed.Url,

                StationName = stationName,
                Description = description,
                Genre = streamInfo.Genre,
                Bitrate = streamInfo.Bitrate
            };
        }
    }
}
