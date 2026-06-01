using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RadioApp.Services.StreamDiscovery
{
    internal class StreamCandidateCollector
    {
        private const int MaxPlayerPagesToCheck = 5;
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
        public async Task<List<string>> ExtractCandidatesFromJsonLikeUrlsAsync(string text, string baseUrl)
        {
            var result = new List<string>();

            var apiUrls = _urlExtractor.ExtractAllUrls(text, baseUrl)
                .Where(_filter.IsPossibleJsonOrApiUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxApiUrlsToCheck)
                .ToList();

            foreach (string apiUrl in apiUrls)
            {
                Log.Debug("Downloading possible JSON/API URL: {ApiUrl}", apiUrl);

                string content = await _httpTextDownloadService.DownloadTextSafeAsync(apiUrl);

                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                result.AddRange(_urlExtractor.ExtractStreamLikeUrls(content, apiUrl));
                result.AddRange(_urlExtractor.ExtractStreamUrlsFromStructuredText(content, apiUrl));
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        public async Task<List<string>> ExtractCandidatesFromJavaScriptFilesAsync(string html, string baseUrl)
        {
            var result = new List<string>();

            var scriptUrls = _urlExtractor.ExtractJavaScriptUrls(html, baseUrl)
                .Take(MaxScriptsToCheck)
                .ToList();

            foreach (string scriptUrl in scriptUrls)
            {
                Log.Debug("Downloading JavaScript file: {ScriptUrl}", scriptUrl);

                string js = await _httpTextDownloadService.DownloadTextSafeAsync(scriptUrl);

                if (string.IsNullOrWhiteSpace(js))
                {
                    continue;
                }

                result.AddRange(_urlExtractor.ExtractStreamLikeUrls(js, scriptUrl));
                result.AddRange(_urlExtractor.ExtractStreamUrlsFromStructuredText(js, scriptUrl));
                result.AddRange(await ExtractCandidatesFromJsonLikeUrlsAsync(js, scriptUrl));
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        public async Task<List<string>> CollectCandidateStreamUrlsAsync(string pageUrl)
        {
            var candidates = new List<string>();

            string html = await _httpTextDownloadService.DownloadTextAsync(pageUrl);

            candidates.AddRange(_urlExtractor.ExtractStreamLikeUrls(html, pageUrl));

            candidates.AddRange(_urlExtractor.ExtractStreamUrlsFromStructuredText(html, pageUrl));

            candidates.AddRange(await ExtractCandidatesFromJavaScriptFilesAsync(html, pageUrl));

            candidates.AddRange(await ExtractCandidatesFromJsonLikeUrlsAsync(html, pageUrl));

            var playerPages = _urlExtractor.ExtractPossiblePlayerPages(html, pageUrl)
                             .Concat(_urlExtractor.ExtractIframeUrls(html, pageUrl))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .Take(MaxPlayerPagesToCheck)
                             .ToList();

            foreach (string playerPage in playerPages)
            {
                Log.Debug("Checking possible player page: {PlayerPage}", playerPage);

                string playerHtml = await _httpTextDownloadService.DownloadTextSafeAsync(playerPage);

                if (string.IsNullOrWhiteSpace(playerHtml))
                {
                    continue;
                }

                candidates.AddRange(_urlExtractor.ExtractStreamLikeUrls(playerHtml, playerPage));
                candidates.AddRange(_urlExtractor.ExtractStreamUrlsFromStructuredText(playerHtml, playerPage));
                candidates.AddRange(await ExtractCandidatesFromJavaScriptFilesAsync(playerHtml, playerPage));
                candidates.AddRange(await ExtractCandidatesFromJsonLikeUrlsAsync(playerHtml, playerPage));
            }

            var secureNetPlayerPages = candidates.Where(_secureNetSystemsDiscoveryService.IsSecureNetSystemsPlayerUrl)
                                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                                 .ToList();

            foreach (string secureNetPlayerPage in secureNetPlayerPages)
            {
                Log.Information("Checking SecureNetSystems player page: {PlayerPage}", secureNetPlayerPage);

                string secureNetHtml = await _httpTextDownloadService.DownloadTextSafeAsync(secureNetPlayerPage);

                if (string.IsNullOrWhiteSpace(secureNetHtml))
                {
                    continue;
                }

                candidates.AddRange(_urlExtractor.ExtractStreamLikeUrls(secureNetHtml, secureNetPlayerPage));
                candidates.AddRange(_urlExtractor.ExtractStreamUrlsFromStructuredText(secureNetHtml, secureNetPlayerPage));
                candidates.AddRange(await ExtractCandidatesFromJavaScriptFilesAsync(secureNetHtml, secureNetPlayerPage));
                candidates.AddRange(await ExtractCandidatesFromJsonLikeUrlsAsync(secureNetHtml, secureNetPlayerPage));
            }

            return candidates.Where(x => !string.IsNullOrWhiteSpace(x))
                             .Select(_urlExtractor.CleanUrl)
                             .Where(x => !_filter.IsDefinitelyNotStreamUrl(x))
                             .Where(x => !_filter.IsRejectedStreamCandidate(x))
                             .Where(_filter.IsPossibleStreamUrl)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .ToList();
        }
    }
}
