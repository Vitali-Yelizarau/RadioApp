using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadioApp.Services.StreamDiscovery
{
    public class StreamCandidateCollector
    {
        private const int MaxJavaScriptFilesToCheck = 20;
        private const int MaxJsonApiUrlsToCheck = 20;
        private const int MaxPlayerPagesToCheck = 10;
        private const int MaxScriptsToCheck = 20;
        private const int MaxApiUrlsToCheck = 20;

        private readonly SecureNetSystemsDiscoveryService _secureNetSystemsDiscoveryService;
        private readonly StreamCandidateFilter _filter;
        private readonly StreamUrlExtractor _urlExtractor;
        private readonly HttpTextDownloadService _httpTextDownloadService;

        public StreamCandidateCollector()
        {
            _filter = new StreamCandidateFilter();
            _urlExtractor = new StreamUrlExtractor();
            _httpTextDownloadService = new HttpTextDownloadService();
            _secureNetSystemsDiscoveryService = new SecureNetSystemsDiscoveryService();
        }

        public async Task<string> DownloadPageHtmlSafeAsync(
                                    string pageUrl,
                                    CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pageUrl))
            {
                return string.Empty;
            }

            return await _httpTextDownloadService.DownloadTextSafeAsync(
                pageUrl,
                cancellationToken
            );
        }
        private async Task<List<string>> ExtractCandidatesFromJsonLikeUrlsAsync(string html, string pageUrl, CancellationToken cancellationToken)
        {
            var candidates = new List<string>();

            if (string.IsNullOrWhiteSpace(html))
            {
                return candidates;
            }

            List<string> apiUrls = _urlExtractor.ExtractPossibleJsonApiUrls(html, pageUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxJsonApiUrlsToCheck)
                .ToList();

            foreach (string apiUrl in apiUrls)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Log.Debug("Downloading possible JSON/API URL: {ApiUrl}", apiUrl);

                string content = await _httpTextDownloadService.DownloadTextSafeAsync(
                    apiUrl,
                    cancellationToken
                );

                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                candidates.AddRange(_urlExtractor.ExtractStreamLikeUrls(content, apiUrl));
                candidates.AddRange(_urlExtractor.ExtractStreamUrlsFromStructuredText(content, apiUrl));
                candidates.AddRange(ExtractDeutschlandFmStreamCandidatesFromText(content));
            }

            return candidates;
        }
        private async Task<List<string>> ExtractCandidatesFromJavaScriptFilesAsync(
                                            string html,
                                            string pageUrl,
                                            CancellationToken cancellationToken)
        {
            var candidates = new List<string>();

            if (string.IsNullOrWhiteSpace(html))
            {
                return candidates;
            }

            List<string> scriptUrls = _urlExtractor.ExtractJavaScriptUrls(html, pageUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxJavaScriptFilesToCheck)
                .ToList();

            foreach (string scriptUrl in scriptUrls)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Log.Debug("Downloading JavaScript file: {ScriptUrl}", scriptUrl);

                string scriptContent = await _httpTextDownloadService.DownloadTextSafeAsync(
                    scriptUrl,
                    cancellationToken
                );

                if (string.IsNullOrWhiteSpace(scriptContent))
                {
                    continue;
                }

                candidates.AddRange(_urlExtractor.ExtractStreamLikeUrls(scriptContent, scriptUrl));
                candidates.AddRange(_urlExtractor.ExtractStreamUrlsFromStructuredText(scriptContent, scriptUrl));
                candidates.AddRange(ExtractDeutschlandFmStreamCandidatesFromText(scriptContent));
            }

            return candidates;
        }
        public async Task<List<string>> CollectCandidateStreamUrlsAsync(string pageUrl, CancellationToken cancellationToken)
        {
            var candidates = new List<string>();

            cancellationToken.ThrowIfCancellationRequested();

            string html = await _httpTextDownloadService.DownloadTextAsync(
                pageUrl,
                cancellationToken
            );

            candidates.AddRange(_urlExtractor.ExtractStreamLikeUrls(html, pageUrl));
            candidates.AddRange(_urlExtractor.ExtractStreamUrlsFromStructuredText(html, pageUrl));
            candidates.AddRange(ExtractDeutschlandFmStreamCandidatesFromText(html));

            candidates.AddRange(await ExtractCandidatesFromPlaylistUrlsAsync(
                html,
                pageUrl,
                cancellationToken
            ));

            candidates.AddRange(await ExtractCandidatesFromJavaScriptFilesAsync(
                html,
                pageUrl,
                cancellationToken
            ));

            candidates.AddRange(await ExtractCandidatesFromJsonLikeUrlsAsync(
                html,
                pageUrl,
                cancellationToken
            ));

            var playerPages = _urlExtractor.ExtractPossiblePlayerPages(html, pageUrl)
                             .Concat(_urlExtractor.ExtractIframeUrls(html, pageUrl))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .Take(MaxPlayerPagesToCheck)
                             .ToList();

            foreach (string playerPage in playerPages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Log.Debug("Checking possible player page: {PlayerPage}", playerPage);

                string playerHtml = await _httpTextDownloadService.DownloadTextSafeAsync(
                    playerPage,
                    cancellationToken
                );

                if (string.IsNullOrWhiteSpace(playerHtml))
                {
                    continue;
                }

                candidates.AddRange(_urlExtractor.ExtractStreamLikeUrls(playerHtml, playerPage));
                candidates.AddRange(_urlExtractor.ExtractStreamUrlsFromStructuredText(playerHtml, playerPage));
                candidates.AddRange(ExtractDeutschlandFmStreamCandidatesFromText(playerHtml));

                candidates.AddRange(await ExtractCandidatesFromJavaScriptFilesAsync(
                    playerHtml,
                    playerPage,
                    cancellationToken
                ));

                candidates.AddRange(await ExtractCandidatesFromJsonLikeUrlsAsync(
                    playerHtml,
                    playerPage,
                    cancellationToken
                ));
            }

            var secureNetPlayerPages = candidates.Where(_secureNetSystemsDiscoveryService.IsSecureNetSystemsPlayerUrl)
                                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                                 .ToList();

            foreach (string secureNetPlayerPage in secureNetPlayerPages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Log.Information("Checking SecureNetSystems player page: {PlayerPage}", secureNetPlayerPage);

                string secureNetHtml = await _httpTextDownloadService.DownloadTextSafeAsync(
                    secureNetPlayerPage,
                    cancellationToken
                );

                if (string.IsNullOrWhiteSpace(secureNetHtml))
                {
                    continue;
                }

                candidates.AddRange(_urlExtractor.ExtractStreamLikeUrls(secureNetHtml, secureNetPlayerPage));
                candidates.AddRange(_urlExtractor.ExtractStreamUrlsFromStructuredText(secureNetHtml, secureNetPlayerPage));
                candidates.AddRange(ExtractDeutschlandFmStreamCandidatesFromText(secureNetHtml));

                candidates.AddRange(await ExtractCandidatesFromJavaScriptFilesAsync(
                    secureNetHtml,
                    secureNetPlayerPage,
                    cancellationToken
                ));

                candidates.AddRange(await ExtractCandidatesFromJsonLikeUrlsAsync(
                    secureNetHtml,
                    secureNetPlayerPage,
                    cancellationToken
                ));
            }

            return candidates.Where(x => !string.IsNullOrWhiteSpace(x))
                             .Select(_urlExtractor.CleanUrl)
                             .Where(x => !_filter.IsDefinitelyNotStreamUrl(x))
                             .Where(x => !_filter.IsRejectedStreamCandidate(x))
                             .Where(_filter.IsPossibleStreamUrl)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .ToList();
        }

        private List<string> ExtractDeutschlandFmStreamCandidatesFromText(string text)
        {
            var candidates = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
            {
                return candidates;
            }

            string normalizedText = text
                .Replace("\\/", "/")
                .Replace("\\u002F", "/")
                .Replace("&amp;", "&");

            var jsonFieldRegex = new System.Text.RegularExpressions.Regex(
                @"""(?:s|jp|ios)""\s*:\s*""(?<url>https?:\/\/[^""]+)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            foreach (System.Text.RegularExpressions.Match match in jsonFieldRegex.Matches(normalizedText))
            {
                string candidate = match.Groups["url"].Value.Trim();

                candidate = CleanDeutschlandFmCandidate(candidate);

                if (IsDeutschlandFmStreamCandidate(candidate) &&
                    !candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    Log.Information(
                        "Deutschland.fm stream candidate found in JSON field. Candidate: {Candidate}",
                        candidate
                    );

                    candidates.Add(candidate);
                }
            }

            var genericUrlRegex = new System.Text.RegularExpressions.Regex(
                @"https?:\/\/[^""'\s<>\\]+",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            foreach (System.Text.RegularExpressions.Match match in genericUrlRegex.Matches(normalizedText))
            {
                string candidate = match.Value.Trim();

                candidate = CleanDeutschlandFmCandidate(candidate);

                if (IsDeutschlandFmStreamCandidate(candidate) &&
                    !candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    Log.Information(
                        "Deutschland.fm stream candidate found in text. Candidate: {Candidate}",
                        candidate
                    );

                    candidates.Add(candidate);
                }
            }

            return candidates;
        }

        private string CleanDeutschlandFmCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return string.Empty;
            }

            return candidate
                .Replace("\\/", "/")
                .Replace("\\u002F", "/")
                .Replace("&amp;", "&")
                .Trim()
                .TrimEnd('.', ',', ';', ')', ']', '}', '"', '\'');
        }

        private bool IsDeutschlandFmStreamCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            string lower = candidate.ToLowerInvariant();

            return lower.Contains("mp3channels.webradio.") ||
                   lower.Contains(".webradio.antenne.de");
        }

        private async Task<List<string>> ExtractCandidatesFromPlaylistUrlsAsync(
                                            string text,
                                            string pageUrl,
                                            CancellationToken cancellationToken)
        {
            var candidates = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
            {
                return candidates;
            }

            List<string> playlistUrls = _urlExtractor.ExtractAllUrls(text, pageUrl)
                .Where(x => IsPlaylistUrl(x) || IsAbsolutRadioM3uApiUrl(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            playlistUrls = PrioritizePlaylistUrlsForPage(
                playlistUrls,
                pageUrl
            );

            playlistUrls = playlistUrls
                .Take(8)
                .ToList();

            foreach (string playlistUrl in playlistUrls)
            {
                cancellationToken.ThrowIfCancellationRequested();

                /*
                 * AbsolutRadio /api/m3u/... behaves like a redirect/stream endpoint.
                 * Do NOT download it as text, otherwise we try to read an endless audio stream.
                 */
                if (IsAbsolutRadioM3uApiUrl(playlistUrl))
                {
                    List<string> absolutCandidates =
                        await BuildAbsolutRadioCandidatesFromM3uApiUrlAsync(
                            playlistUrl,
                            cancellationToken
                        );

                    candidates.AddRange(absolutCandidates);
                    continue;
                }

                Log.Debug("Downloading playlist URL: {PlaylistUrl}", playlistUrl);

                string playlistContent = await _httpTextDownloadService.DownloadTextSafeAsync(
                    playlistUrl,
                    cancellationToken
                );

                if (string.IsNullOrWhiteSpace(playlistContent))
                {
                    continue;
                }

                candidates.AddRange(ExtractStreamUrlsFromPlaylistContent(
                    playlistContent,
                    playlistUrl
                ));
            }

            return candidates
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(_urlExtractor.CleanUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool IsAbsolutRadioM3uApiUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath.ToLowerInvariant();

            return host.Contains("absolutradio.de") &&
                   path.Contains("/api/m3u/");
        }

        private List<string> PrioritizePlaylistUrlsForPage(
                                List<string> playlistUrls,
                                string pageUrl)
        {
            if (playlistUrls == null || playlistUrls.Count == 0)
            {
                return new List<string>();
            }

            List<string> pageHints = BuildPageSlugHints(pageUrl);

            return playlistUrls
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => GetPlaylistUrlPriorityScore(x, pageHints))
                .ToList();
        }

        private int GetPlaylistUrlPriorityScore(
                        string playlistUrl,
                        List<string> pageHints)
        {
            if (string.IsNullOrWhiteSpace(playlistUrl))
            {
                return -1000;
            }

            string lower = playlistUrl.ToLowerInvariant();

            int score = 0;

            if (IsAbsolutRadioM3uApiUrl(playlistUrl))
            {
                score += 300;
            }

            if (lower.Contains(".m3u"))
            {
                score += 100;
            }

            foreach (string hint in pageHints)
            {
                if (string.IsNullOrWhiteSpace(hint))
                {
                    continue;
                }

                if (lower.Contains(hint.ToLowerInvariant()))
                {
                    score += 1000;
                }
            }

            /*
             * Known AbsolutRadio mapping:
             * page: /sender/oldies
             * stream/API often contains: oldieclassics or oldies
             */
            if (pageHints.Contains("oldies", StringComparer.OrdinalIgnoreCase))
            {
                if (lower.Contains("oldies") ||
                    lower.Contains("oldie") ||
                    lower.Contains("oldieclassics"))
                {
                    score += 1500;
                }
            }

            if (pageHints.Contains("coffeemusic", StringComparer.OrdinalIgnoreCase))
            {
                if (lower.Contains("coffeemusic") ||
                    lower.Contains("coffee"))
                {
                    score += 1500;
                }
            }

            /*
             * Penalize unrelated Absolut stations when the page clearly targets one station.
             */
            if (pageHints.Count > 0 &&
                IsAbsolutRadioM3uApiUrl(playlistUrl))
            {
                if (lower.Contains("musicxl") ||
                    lower.Contains("80er") ||
                    lower.Contains("hot") ||
                    lower.Contains("germany") ||
                    lower.Contains("top") ||
                    lower.Contains("relax") ||
                    lower.Contains("bella") ||
                    lower.Contains("clubnight"))
                {
                    bool matchesCurrentPage = pageHints.Any(h =>
                        lower.Contains(h.ToLowerInvariant()));

                    if (!matchesCurrentPage)
                    {
                        score -= 500;
                    }
                }
            }

            return score;
        }

        private List<string> BuildPageSlugHints(string pageUrl)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(pageUrl))
            {
                return result;
            }

            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri uri))
            {
                return result;
            }

            string[] segments = uri.AbsolutePath
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToLowerInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            foreach (string segment in segments)
            {
                if (segment == "sender" ||
                    segment == "stream" ||
                    segment == "radio" ||
                    segment == "webradio")
                {
                    continue;
                }

                result.Add(segment);
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<string>> BuildAbsolutRadioCandidatesFromM3uApiUrlAsync(
                                            string playlistUrl,
                                            CancellationToken cancellationToken)
        {
            var candidates = new List<string>();

            if (string.IsNullOrWhiteSpace(playlistUrl))
            {
                return candidates;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                Log.Debug(
                    "Resolving AbsolutRadio M3U API URL without reading stream body: {PlaylistUrl}",
                    playlistUrl
                );

                string finalUrl = await _httpTextDownloadService.GetFinalUrlAsync(
                    playlistUrl,
                    cancellationToken
                );

                if (!string.IsNullOrWhiteSpace(finalUrl))
                {
                    string cleanedFinalUrl = _urlExtractor.CleanUrl(finalUrl);

                    if (!_filter.IsRejectedStreamCandidate(cleanedFinalUrl) &&
                        !_filter.IsRejectedFinalStreamUrl(cleanedFinalUrl) &&
                        !_filter.IsDefinitelyNotStreamUrl(cleanedFinalUrl))
                    {
                        Log.Information(
                            "AbsolutRadio stream candidate resolved from M3U API. ApiUrl: {ApiUrl}, FinalUrl: {FinalUrl}",
                            playlistUrl,
                            cleanedFinalUrl
                        );

                        candidates.Add(cleanedFinalUrl);
                    }
                }

                /*
                 * Also keep the API URL itself as a fallback candidate.
                 * VLC can usually follow this redirect and play it.
                 */
                string cleanedApiUrl = _urlExtractor.CleanUrl(playlistUrl);

                if (!string.IsNullOrWhiteSpace(cleanedApiUrl))
                {
                    candidates.Add(cleanedApiUrl);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(
                    ex,
                    "Failed to resolve AbsolutRadio M3U API URL: {PlaylistUrl}",
                    playlistUrl
                );
            }

            return candidates
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> ExtractStreamUrlsFromPlaylistContent(
            string playlistContent,
            string playlistUrl)
        {
            var candidates = new List<string>();

            if (string.IsNullOrWhiteSpace(playlistContent))
            {
                return candidates;
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

                int equalsIndex = line.IndexOf('=');

                if (equalsIndex >= 0)
                {
                    string key = line.Substring(0, equalsIndex).Trim();

                    if (key.StartsWith("File", StringComparison.OrdinalIgnoreCase))
                    {
                        line = line.Substring(equalsIndex + 1).Trim();
                    }
                }

                if (!Uri.IsWellFormedUriString(line, UriKind.Absolute))
                {
                    continue;
                }

                string cleaned = _urlExtractor.CleanUrl(line);

                if (_filter.IsPossibleStreamUrl(cleaned) &&
                    !_filter.IsRejectedStreamCandidate(cleaned) &&
                    !_filter.IsDefinitelyNotStreamUrl(cleaned))
                {
                    Log.Information(
                        "Stream candidate found in playlist. PlaylistUrl: {PlaylistUrl}, Candidate: {Candidate}",
                        playlistUrl,
                        cleaned
                    );

                    candidates.Add(cleaned);
                }
            }

            return candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
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
                || lower.Contains(".pls?")
                || lower.Contains("/api/m3u/");
        }
    }
}
