using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadioApp.Services.StreamDiscovery
{
    internal class StreamCandidateCollector
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
    }
}
