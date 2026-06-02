using RadioApp.Models;
using RadioApp.Services.StreamDiscovery;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RadioApp.Services
{
    public class InternetRadioDirectoryDiscoveryService
    {
        private const int MaxDirectoryPagesToCheck = 5;
        private const int MaxCandidatesToCheck = 15;

        private readonly HttpTextDownloadService _httpTextDownloadService;
        private readonly StreamUrlExtractor _urlExtractor;
        private readonly StreamCandidateFilter _filter;
        private readonly RadioStreamInfoService _streamInfoService;

        public InternetRadioDirectoryDiscoveryService()
        {
            _httpTextDownloadService = new HttpTextDownloadService();
            _urlExtractor = new StreamUrlExtractor();
            _filter = new StreamCandidateFilter();
            _streamInfoService = new RadioStreamInfoService();
        }

        public async Task<DiscoveredRadioStream> TryDiscoverAsync(
            string originalPageUrl,
            string originalHtml,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(originalPageUrl))
            {
                return null;
            }

            List<string> stationSlugs = ExtractPossibleStationSlugs(originalPageUrl, originalHtml);

            if (stationSlugs.Count == 0)
            {
                Log.Information(
                    "Internet-Radio.com fallback skipped because no station slug could be extracted. PageUrl: {PageUrl}",
                    originalPageUrl
                );

                return null;
            }

            List<string> directoryPageUrls = BuildInternetRadioDirectoryPageUrls(
                                                originalPageUrl,
                                                stationSlugs
                                            );

            Log.Information(
                "Internet-Radio.com fallback started. PageUrl: {PageUrl}, Slugs: {Slugs}",
                originalPageUrl,
                string.Join(", ", stationSlugs)
            );

            foreach (string directoryPageUrl in directoryPageUrls.Take(MaxDirectoryPagesToCheck))
            {
                cancellationToken.ThrowIfCancellationRequested();

                Log.Information(
                    "Checking Internet-Radio.com directory page: {DirectoryPageUrl}",
                    directoryPageUrl
                );

                string directoryHtml = await _httpTextDownloadService.DownloadTextSafeAsync(
                    directoryPageUrl,
                    cancellationToken
                );

                if (string.IsNullOrWhiteSpace(directoryHtml))
                {
                    continue;
                }

                if (!LooksLikeRelevantInternetRadioPage(directoryHtml, stationSlugs))
                {
                    Log.Debug(
                        "Internet-Radio.com page does not look relevant. DirectoryPageUrl: {DirectoryPageUrl}",
                        directoryPageUrl
                    );

                    continue;
                }

                List<string> candidates = await ExtractStreamCandidatesFromDirectoryPageAsync(
                    directoryHtml,
                    directoryPageUrl,
                    cancellationToken
                );

                candidates = candidates
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(_urlExtractor.CleanUrl)
                    .Where(x => !_filter.IsRejectedStreamCandidate(x))
                    .Where(x => !_filter.IsRejectedFinalStreamUrl(x))
                    .Where(x => !_filter.IsDefinitelyNotStreamUrl(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                candidates = PrioritizeCandidates(candidates, stationSlugs);

                Log.Information(
                    "Internet-Radio.com fallback produced {Count} stream candidates. DirectoryPageUrl: {DirectoryPageUrl}",
                    candidates.Count,
                    directoryPageUrl
                );

                foreach (string candidate in candidates.Take(10))
                {
                    Log.Information(
                        "Internet-Radio.com candidate: {Candidate}",
                        candidate
                    );
                }

                foreach (string candidate in candidates.Take(MaxCandidatesToCheck))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    DiscoveredRadioStream streamInfo =
                        await _streamInfoService.GetStreamInfoIfPlayableAsync(
                            originalPageUrl,
                            candidate
                        );

                    if (streamInfo == null)
                    {
                        continue;
                    }

                    if (_filter.IsRejectedStreamCandidate(streamInfo.StreamUrl) ||
                        _filter.IsRejectedFinalStreamUrl(streamInfo.StreamUrl) ||
                        _filter.IsDefinitelyNotStreamUrl(streamInfo.StreamUrl))
                    {
                        Log.Debug(
                            "Internet-Radio.com confirmed candidate rejected after final URL check. Candidate: {Candidate}, FinalUrl: {FinalUrl}",
                            candidate,
                            streamInfo.StreamUrl
                        );

                        continue;
                    }

                    string stationName = streamInfo.StationName;

                    if (string.IsNullOrWhiteSpace(stationName))
                    {
                        stationName = ExtractStationNameFromDirectoryPage(directoryHtml);
                    }

                    if (string.IsNullOrWhiteSpace(stationName))
                    {
                        stationName = BuildFallbackStationName(stationSlugs.FirstOrDefault());
                    }

                    string description = streamInfo.Description;

                    if (string.IsNullOrWhiteSpace(description))
                    {
                        description = "Detected via Internet-Radio.com directory fallback.";
                    }

                    var result = new DiscoveredRadioStream
                    {
                        PageUrl = originalPageUrl,

                        /*
                         * ВАЖНО:
                         * Сохраняем исходный candidate URL, а не redirect target.
                         */
                        StreamUrl = candidate,

                        StationName = stationName,
                        Description = description,
                        Genre = streamInfo.Genre,
                        Bitrate = streamInfo.Bitrate
                    };

                    Log.Information(
                        "Internet-Radio.com fallback selected stream. SavedStreamUrl: {SavedStreamUrl}, FinalCheckedUrl: {FinalCheckedUrl}, StationName: {StationName}",
                        result.StreamUrl,
                        streamInfo.StreamUrl,
                        result.StationName
                    );

                    return result;
                }
            }

            Log.Information(
                "Internet-Radio.com fallback did not find a playable stream. PageUrl: {PageUrl}",
                originalPageUrl
            );

            return null;
        }

        public bool IsDirectInternetRadioStationPage(string pageUrl)
        {
            if (string.IsNullOrWhiteSpace(pageUrl))
            {
                return false;
            }

            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath.ToLowerInvariant();

            return host.Contains("internet-radio.com") &&
                   path.StartsWith("/station/");
        }

        private List<string> ExtractPossibleStationSlugs(
            string originalPageUrl,
            string originalHtml)
        {
            var slugs = new List<string>();

            if (!Uri.TryCreate(originalPageUrl, UriKind.Absolute, out Uri uri))
            {
                return slugs;
            }

            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath.Trim('/');

            string[] parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            /*
             * internet-radio.com direct page:
             * https://www.internet-radio.com/station/undertowradio/
             * => undertowradio
             */
            if (host.Contains("internet-radio.com"))
            {
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (parts[i].Equals("station", StringComparison.OrdinalIgnoreCase))
                    {
                        AddSlug(slugs, parts[i + 1]);
                    }
                }
            }

            /*
             * radio.net:
             * https://www.radio.net/s/bloodstream
             * => bloodstream
             */
            if (host.Contains("radio.net"))
            {
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (parts[i].Equals("s", StringComparison.OrdinalIgnoreCase))
                    {
                        AddSlug(slugs, parts[i + 1]);
                    }
                }

                if (slugs.Count == 0 && parts.Length > 0)
                {
                    AddSlug(slugs, parts[parts.Length - 1]);
                }
            }

            /*
             * official site:
             * https://radiobloodstream.com/
             * => radiobloodstream
             * => bloodstream
             *
             * But do NOT do this for internet-radio.com itself,
             * otherwise we would add "internet-radio" as a fake station slug.
             */
            if (!host.Contains("internet-radio.com"))
            {
                string hostWithoutWww = host;

                if (hostWithoutWww.StartsWith("www."))
                {
                    hostWithoutWww = hostWithoutWww.Substring(4);
                }

                string domainName = hostWithoutWww.Split('.')[0];

                AddSlug(slugs, domainName);

                if (domainName.StartsWith("radio") && domainName.Length > "radio".Length)
                {
                    AddSlug(slugs, domainName.Substring("radio".Length));
                }

                if (domainName.EndsWith("radio") && domainName.Length > "radio".Length)
                {
                    AddSlug(slugs, domainName.Substring(0, domainName.Length - "radio".Length));
                }
            }

            string pageTitle = ExtractHtmlTitle(originalHtml);

            if (!string.IsNullOrWhiteSpace(pageTitle))
            {
                foreach (string slug in BuildSlugsFromText(pageTitle))
                {
                    AddSlug(slugs, slug);
                }
            }

            return slugs
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> BuildInternetRadioDirectoryPageUrls(
            string originalPageUrl,
            List<string> stationSlugs)
        {
            var urls = new List<string>();

            /*
             * If the user already gave us an Internet-Radio.com station page,
             * check this exact page first.
             */
            if (IsDirectInternetRadioStationPage(originalPageUrl))
            {
                urls.Add(originalPageUrl);
            }

            foreach (string slug in stationSlugs)
            {
                if (string.IsNullOrWhiteSpace(slug))
                {
                    continue;
                }

                string cleanedSlug = NormalizeSlug(slug);

                if (string.IsNullOrWhiteSpace(cleanedSlug))
                {
                    continue;
                }

                urls.Add("https://www.internet-radio.com/station/" + cleanedSlug + "/");
            }

            return urls
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<string>> ExtractStreamCandidatesFromDirectoryPageAsync(
            string directoryHtml,
            string directoryPageUrl,
            CancellationToken cancellationToken)
        {
            var candidates = new List<string>();

            string normalizedHtml = NormalizeText(directoryHtml);

            candidates.AddRange(ExtractDirectInternetRadioStreamUrls(normalizedHtml));
            candidates.AddRange(ExtractProxyInternetRadioStreamUrls(normalizedHtml));
            candidates.AddRange(ExtractEncodedUrls(normalizedHtml));

            List<string> allUrls = _urlExtractor.ExtractAllUrls(normalizedHtml, directoryPageUrl);

            foreach (string url in allUrls)
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                string cleaned = _urlExtractor.CleanUrl(url);

                if (IsPlaylistUrl(cleaned))
                {
                    candidates.AddRange(await ExtractCandidatesFromPlaylistUrlAsync(
                        cleaned,
                        cancellationToken
                    ));

                    continue;
                }

                if (LooksLikeInternetRadioStreamUrl(cleaned))
                {
                    candidates.Add(cleaned);
                }
            }

            /*
             * Some pages contain escaped or query-encoded playlist URLs.
             */
            List<string> encodedUrls = ExtractEncodedUrls(normalizedHtml);

            foreach (string encodedUrl in encodedUrls)
            {
                if (string.IsNullOrWhiteSpace(encodedUrl))
                {
                    continue;
                }

                string cleaned = _urlExtractor.CleanUrl(encodedUrl);

                if (IsPlaylistUrl(cleaned))
                {
                    candidates.AddRange(await ExtractCandidatesFromPlaylistUrlAsync(
                        cleaned,
                        cancellationToken
                    ));

                    continue;
                }

                if (LooksLikeInternetRadioStreamUrl(cleaned))
                {
                    candidates.Add(cleaned);
                }
            }

            return candidates
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(_urlExtractor.CleanUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> ExtractDirectInternetRadioStreamUrls(string text)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            /*
             * Examples:
             * http://uk1.internet-radio.com:8294/stream
             * http://uk1.internet-radio.com:8294/live
             * https://uk1.internet-radio.com:8294/stream
             */
            var regex = new Regex(
                @"https?:\/\/[a-z0-9.-]*internet-radio\.com:\d+\/(?:stream|live)(?:[^\s""'<>)]*)?",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in regex.Matches(text))
            {
                string candidate = CleanCandidate(match.Value);

                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    result.Add(candidate);
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> ExtractProxyInternetRadioStreamUrls(string text)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            /*
             * Example:
             * https://uk1.internet-radio.com/proxy/bloodstream?mp=%2Fstream
             */
            var regex = new Regex(
                @"https?:\/\/[a-z0-9.-]*internet-radio\.com\/proxy\/[^""'\s<>)]*",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in regex.Matches(text))
            {
                string candidate = CleanCandidate(match.Value);

                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    result.Add(candidate);
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> ExtractEncodedUrls(string text)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            /*
             * Finds encoded urls inside query parameters:
             * ?u=http%3A%2F%2Fuk1.internet-radio.com%3A8294%2Fstream.m3u
             * &url=http%3A%2F%2F...
             * "stream":"http:\/\/uk1.internet-radio.com:8294\/stream"
             */
            var queryRegex = new Regex(
                @"(?:u|url|stream|src|href|file)=([^&""'<>]+)",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in queryRegex.Matches(text))
            {
                string rawValue = match.Groups[1].Value;

                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    continue;
                }

                string decoded = WebUtility.UrlDecode(rawValue);

                decoded = NormalizeText(decoded);
                decoded = CleanCandidate(decoded);

                if (Uri.IsWellFormedUriString(decoded, UriKind.Absolute))
                {
                    result.Add(decoded);
                }
            }

            var escapedUrlRegex = new Regex(
                @"https?:\\\/\\\/[^""'\s<>]+",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in escapedUrlRegex.Matches(text))
            {
                string decoded = NormalizeText(match.Value);
                decoded = CleanCandidate(decoded);

                if (Uri.IsWellFormedUriString(decoded, UriKind.Absolute))
                {
                    result.Add(decoded);
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<string>> ExtractCandidatesFromPlaylistUrlAsync(
            string playlistUrl,
            CancellationToken cancellationToken)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(playlistUrl))
            {
                return result;
            }

            cancellationToken.ThrowIfCancellationRequested();

            Log.Debug(
                "Internet-Radio.com fallback downloading playlist URL: {PlaylistUrl}",
                playlistUrl
            );

            string playlistContent = await _httpTextDownloadService.DownloadTextSafeAsync(
                playlistUrl,
                cancellationToken
            );

            if (string.IsNullOrWhiteSpace(playlistContent))
            {
                return result;
            }

            result.AddRange(ExtractStreamUrlsFromPlaylistContent(
                playlistContent,
                playlistUrl
            ));

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> ExtractStreamUrlsFromPlaylistContent(
            string playlistContent,
            string playlistUrl)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(playlistContent))
            {
                return result;
            }

            string[] lines = playlistContent
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n');

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("#"))
                {
                    continue;
                }

                /*
                 * PLS format:
                 * File1=http://...
                 */
                int equalsIndex = line.IndexOf('=');

                if (equalsIndex >= 0)
                {
                    string key = line.Substring(0, equalsIndex).Trim();

                    if (key.StartsWith("File", StringComparison.OrdinalIgnoreCase))
                    {
                        line = line.Substring(equalsIndex + 1).Trim();
                    }
                }

                line = NormalizeText(line);
                line = CleanCandidate(line);

                if (!Uri.IsWellFormedUriString(line, UriKind.Absolute))
                {
                    continue;
                }

                if (LooksLikeInternetRadioStreamUrl(line) ||
                    _filter.IsPossibleStreamUrl(line))
                {
                    Log.Information(
                        "Internet-Radio.com stream candidate found in playlist. PlaylistUrl: {PlaylistUrl}, Candidate: {Candidate}",
                        playlistUrl,
                        line
                    );

                    result.Add(line);
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> PrioritizeCandidates(
            List<string> candidates,
            List<string> stationSlugs)
        {
            return candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => CalculateCandidateScore(x, stationSlugs))
                .ToList();
        }

        private int CalculateCandidateScore(
            string candidate,
            List<string> stationSlugs)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return -1000;
            }

            string lower = candidate.ToLowerInvariant();

            int score = 0;

            if (lower.Contains("internet-radio.com"))
            {
                score += 300;
            }

            if (lower.Contains(":"))
            {
                score += 50;
            }

            if (lower.Contains("/stream"))
            {
                score += 150;
            }

            if (lower.Contains("/live"))
            {
                score += 120;
            }

            if (lower.Contains("/proxy/"))
            {
                score += 100;
            }

            if (lower.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }

            foreach (string slug in stationSlugs)
            {
                if (string.IsNullOrWhiteSpace(slug))
                {
                    continue;
                }

                string normalizedSlug = NormalizeSlug(slug);

                if (string.IsNullOrWhiteSpace(normalizedSlug))
                {
                    continue;
                }

                if (lower.Contains(normalizedSlug))
                {
                    score += 500;
                }
            }

            return score;
        }

        private bool LooksLikeInternetRadioStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string lower = url.ToLowerInvariant();
            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath.ToLowerInvariant();

            if (!host.Contains("internet-radio.com"))
            {
                return false;
            }

            return uri.Port > 0 ||
                   path.Contains("/stream") ||
                   path.Contains("/live") ||
                   path.Contains("/proxy/");
        }

        private bool IsPlaylistUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string lower = url.ToLowerInvariant();
            string path = uri.AbsolutePath.ToLowerInvariant();

            return path.EndsWith(".m3u")
                || path.EndsWith(".m3u8")
                || path.EndsWith(".pls")
                || lower.Contains(".m3u?")
                || lower.Contains(".m3u8?")
                || lower.Contains(".pls?");
        }

        private bool LooksLikeRelevantInternetRadioPage(
            string html,
            List<string> stationSlugs)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            string lower = html.ToLowerInvariant();

            if (!lower.Contains("internet-radio"))
            {
                return false;
            }

            foreach (string slug in stationSlugs)
            {
                if (string.IsNullOrWhiteSpace(slug))
                {
                    continue;
                }

                string normalizedSlug = NormalizeSlug(slug);

                if (string.IsNullOrWhiteSpace(normalizedSlug))
                {
                    continue;
                }

                if (lower.Contains(normalizedSlug))
                {
                    return true;
                }
            }

            /*
             * Some pages may not contain the exact slug in plain text,
             * but if they contain internet-radio stream hosts, we can still try.
             */
            return lower.Contains("internet-radio.com:") ||
                   lower.Contains("/proxy/");
        }

        private string ExtractStationNameFromDirectoryPage(string html)
        {
            string title = ExtractHtmlTitle(html);

            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            title = WebUtility.HtmlDecode(title);

            title = title
                .Replace("Internet Radio", string.Empty)
                .Replace("Free Internet Radio", string.Empty)
                .Replace("Online", string.Empty)
                .Replace("|", " ")
                .Replace("-", " ")
                .Trim();

            title = Regex.Replace(title, @"\s+", " ");

            return title;
        }

        private string ExtractHtmlTitle(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            Match match = Regex.Match(
                html,
                @"<title[^>]*>(?<title>.*?)<\/title>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );

            if (!match.Success)
            {
                return string.Empty;
            }

            string title = match.Groups["title"].Value;

            title = WebUtility.HtmlDecode(title);
            title = Regex.Replace(title, @"\s+", " ").Trim();

            return title;
        }

        private string BuildFallbackStationName(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return "Internet Radio Station";
            }

            string normalized = NormalizeSlug(slug)
                .Replace("-", " ")
                .Replace("_", " ")
                .Trim();

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "Internet Radio Station";
            }

            return string.Join(
                " ",
                normalized
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => char.ToUpperInvariant(x[0]) + x.Substring(1))
            );
        }

        private List<string> BuildSlugsFromText(string text)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            string cleaned = WebUtility.HtmlDecode(text)
                .ToLowerInvariant();

            cleaned = Regex.Replace(cleaned, @"[^a-z0-9]+", " ");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return result;
            }

            string[] words = cleaned
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(x => x.Length >= 3)
                .ToArray();

            if (words.Length == 0)
            {
                return result;
            }

            result.Add(string.Join("-", words));

            foreach (string word in words)
            {
                result.Add(word);
            }

            return result;
        }

        private void AddSlug(List<string> slugs, string slug)
        {
            string normalized = NormalizeSlug(slug);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (!slugs.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                slugs.Add(normalized);
            }
        }

        private string NormalizeSlug(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string decoded = WebUtility.UrlDecode(value);

            decoded = decoded
                .Trim()
                .ToLowerInvariant();

            decoded = Regex.Replace(decoded, @"[^a-z0-9]+", "-");
            decoded = Regex.Replace(decoded, @"-+", "-");
            decoded = decoded.Trim('-');

            return decoded;
        }

        private string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text
                .Replace("\\/", "/")
                .Replace("\\u002F", "/")
                .Replace("&amp;", "&");
        }

        private string CleanCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return string.Empty;
            }

            candidate = NormalizeText(candidate);
            candidate = WebUtility.HtmlDecode(candidate);

            candidate = candidate
                .Trim()
                .TrimEnd('.', ',', ';', ')', ']', '}', '"', '\'');

            return candidate;
        }

        public bool CanUseAsFallbackFor(string pageUrl)
        {
            if (string.IsNullOrWhiteSpace(pageUrl))
            {
                return false;
            }

            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath.ToLowerInvariant();

            if (host.Contains("internet-radio.com") && path.StartsWith("/station/"))
            {
                return true;
            }

            if (host.Contains("radio.net") && path.StartsWith("/s/"))
            {
                return true;
            }

            return false;
        }
    }
}