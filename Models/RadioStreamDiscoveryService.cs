using RadioApp.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RadioApp.Services
{
    public class RadioStreamDiscoveryService
    {
        private class StreamCandidateEvaluation
        {
            public string Url { get; set; }
            public int Score { get; set; }
            public int? Bitrate { get; set; }
            public bool IsHttpConfirmed { get; set; }
            public DiscoveredRadioStream StreamInfo { get; set; }
            public string Reason { get; set; }
        }

        private const int MaxPlayerPagesToCheck = 5;
        private const int MaxScriptsToCheck = 20;
        private const int MaxApiUrlsToCheck = 20;
        private const int SecureNetFallbackMaxIceServerNumber = 99;
        private const int SecureNetFallbackProbeTimeoutSeconds = 2;
        private const int SecureNetFallbackBatchSize = 20;
        private const int HTTP_TIMEOUT = 15;

        private readonly RadioStreamInfoService _streamInfoService;

        public RadioStreamDiscoveryService()
        {
            _streamInfoService = new RadioStreamInfoService();
        }

        public async Task<DiscoveredRadioStream> DiscoverAsync(string pageUrl)
        {
            Log.Information("Stream discovery started. PageUrl: {PageUrl}", pageUrl);

            var candidates = await CollectCandidateStreamUrlsAsync(pageUrl);

            Log.Information(
                "Collected {Count} candidate stream URLs for PageUrl: {PageUrl}",
                candidates.Count,
                pageUrl
            );

            candidates = SortCandidatesByPriority(candidates);

            string htmlForFallback = await DownloadTextSafeAsync(pageUrl);

            DiscoveredRadioStream secureNetResult =
                await TryDiscoverSecureNetDirectStreamAsync(pageUrl, candidates);

            if (secureNetResult != null)
            {
                return secureNetResult;
            }

            DiscoveredRadioStream amperwaveResult =
                await TryDiscoverAmperwaveDirectStreamAsync(pageUrl, candidates);

            if (amperwaveResult != null)
            {
                return amperwaveResult;
            }

            DiscoveredRadioStream secureNetByCallSignResult =
                await TryDiscoverSecureNetFromCallSignAsync(pageUrl, htmlForFallback);

            if (secureNetByCallSignResult != null)
            {
                return secureNetByCallSignResult;
            }

            DiscoveredRadioStream genericResult =
                await TryDiscoverBestGenericCandidateAsync(pageUrl, candidates, htmlForFallback);

            if (genericResult != null)
            {
                return genericResult;
            }

            throw new InvalidOperationException("Could not find a playable stream URL on this page.");
        }

        private async Task<List<string>> CollectCandidateStreamUrlsAsync(string pageUrl)
        {
            var candidates = new List<string>();

            string html = await DownloadTextAsync(pageUrl);

            candidates.AddRange(ExtractStreamLikeUrls(html, pageUrl));

            candidates.AddRange(ExtractStreamUrlsFromStructuredText(html, pageUrl));

            candidates.AddRange(await ExtractCandidatesFromJavaScriptFilesAsync(html, pageUrl));

            candidates.AddRange(await ExtractCandidatesFromJsonLikeUrlsAsync(html, pageUrl));

            var playerPages = ExtractPossiblePlayerPages(html, pageUrl)
                             .Concat(ExtractIframeUrls(html, pageUrl))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .Take(MaxPlayerPagesToCheck)
                             .ToList();

            foreach (string playerPage in playerPages)
            {
                Log.Debug("Checking possible player page: {PlayerPage}", playerPage);

                string playerHtml = await DownloadTextSafeAsync(playerPage);

                if (string.IsNullOrWhiteSpace(playerHtml))
                {
                    continue;
                }

                candidates.AddRange(ExtractStreamLikeUrls(playerHtml, playerPage));
                candidates.AddRange(ExtractStreamUrlsFromStructuredText(playerHtml, playerPage));
                candidates.AddRange(await ExtractCandidatesFromJavaScriptFilesAsync(playerHtml, playerPage));
                candidates.AddRange(await ExtractCandidatesFromJsonLikeUrlsAsync(playerHtml, playerPage));
            }

            var secureNetPlayerPages = candidates.Where(IsSecureNetSystemsPlayerUrl)
                                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                                 .ToList();

            foreach (string secureNetPlayerPage in secureNetPlayerPages)
            {
                Log.Information("Checking SecureNetSystems player page: {PlayerPage}", secureNetPlayerPage);

                string secureNetHtml = await DownloadTextSafeAsync(secureNetPlayerPage);

                if (string.IsNullOrWhiteSpace(secureNetHtml))
                {
                    continue;
                }

                candidates.AddRange(ExtractStreamLikeUrls(secureNetHtml, secureNetPlayerPage));
                candidates.AddRange(ExtractStreamUrlsFromStructuredText(secureNetHtml, secureNetPlayerPage));
                candidates.AddRange(await ExtractCandidatesFromJavaScriptFilesAsync(secureNetHtml, secureNetPlayerPage));
                candidates.AddRange(await ExtractCandidatesFromJsonLikeUrlsAsync(secureNetHtml, secureNetPlayerPage));
            }

            return candidates.Where(x => !string.IsNullOrWhiteSpace(x))
                             .Select(CleanUrl)
                             .Where(x => !IsDefinitelyNotStreamUrl(x))
                             .Where(x => !IsRejectedStreamCandidate(x))
                             .Where(IsPossibleStreamUrl)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .ToList();
        }

        private async Task<List<string>> ExtractCandidatesFromJavaScriptFilesAsync(string html, string baseUrl)
        {
            var result = new List<string>();

            var scriptUrls = ExtractJavaScriptUrls(html, baseUrl)
                .Take(MaxScriptsToCheck)
                .ToList();

            foreach (string scriptUrl in scriptUrls)
            {
                Log.Debug("Downloading JavaScript file: {ScriptUrl}", scriptUrl);

                string js = await DownloadTextSafeAsync(scriptUrl);

                if (string.IsNullOrWhiteSpace(js))
                {
                    continue;
                }

                result.AddRange(ExtractStreamLikeUrls(js, scriptUrl));
                result.AddRange(ExtractStreamUrlsFromStructuredText(js, scriptUrl));
                result.AddRange(await ExtractCandidatesFromJsonLikeUrlsAsync(js, scriptUrl));
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<string>> ExtractCandidatesFromJsonLikeUrlsAsync(string text, string baseUrl)
        {
            var result = new List<string>();

            var apiUrls = ExtractAllUrls(text, baseUrl)
                .Where(IsPossibleJsonOrApiUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxApiUrlsToCheck)
                .ToList();

            foreach (string apiUrl in apiUrls)
            {
                Log.Debug("Downloading possible JSON/API URL: {ApiUrl}", apiUrl);

                string content = await DownloadTextSafeAsync(apiUrl);

                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                result.AddRange(ExtractStreamLikeUrls(content, apiUrl)); 
                result.AddRange(ExtractStreamUrlsFromStructuredText(content, apiUrl));
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> ExtractStreamLikeUrls(string text, string baseUrl)
        {
            var allUrls = ExtractAllUrls(text, baseUrl);

            return allUrls
                .Where(IsPossibleStreamUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> ExtractAllUrls(string text, string baseUrl)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            var matches = Regex.Matches(
                text,
                @"(?:(?:https?:)?//|/)[^'""<>\s\\]+",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in matches)
            {
                string rawUrl = match.Value.Trim();

                try
                {
                    string normalizedUrl = NormalizeUrl(rawUrl, baseUrl);
                    result.Add(normalizedUrl);
                }
                catch
                {
                    // Ignore invalid URL candidates.
                }
            }

            return result;
        }

        private List<string> ExtractPossiblePlayerPages(string html, string baseUrl)
        {
            return ExtractAllUrls(html, baseUrl)
                .Where(IsPossiblePlayerPageUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> ExtractJavaScriptUrls(string html, string baseUrl)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(html))
            {
                return result;
            }

            var matches = Regex.Matches(
                html,
                @"<script[^>]+src=[""'](?<src>[^""']+)[""']",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in matches)
            {
                string src = match.Groups["src"].Value;

                try
                {
                    string url = NormalizeUrl(src, baseUrl);

                    if (url.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                        url.IndexOf(".js?", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Add(url);
                    }
                }
                catch
                {
                    // Ignore invalid script URL.
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool IsPossibleStreamUrl(string url)
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

            return lower.Contains(".mp3")
                || lower.Contains(".aac")
                || lower.Contains(".ogg")
                || lower.Contains(".opus")
                || lower.Contains(".m3u8")
                || lower.Contains(".pls")
                || lower.Contains(".m3u")

                || path.Contains("mp3-")
                || path.Contains("aac-")
                || path.Contains("opus-")
                || path.Contains("ogg-")

                || path.Contains("/stream")
                || path.Contains("/streams")
                || path.Contains("/live")
                || path.Contains("/listen")
                || path.Contains("/radio")
                || path.Contains("/manifest")

                || host.StartsWith("online.")
                || host.StartsWith("stream.")
                || host.StartsWith("streams.")
                || host.Contains(".stream.")
                || host.Contains(".streams.")

                || lower.Contains("tritondigital")
                || lower.Contains("streamguys")
                || lower.Contains("icecast")
                || lower.Contains("shoutcast")
                || lower.Contains("securenetsystems.net/v5/")
                || lower.Contains("envisionwise")
                || lower.Contains("playerservices")
                || lower.Contains("amperwave");
        }

        private bool IsPossiblePlayerPageUrl(string url)
        {
            string lower = url.ToLowerInvariant();

            return lower.Contains("player")
                || lower.Contains("play.")
                || lower.Contains("/play")
                || lower.Contains("listen")
                || lower.Contains("online");
        }

        private bool IsPossibleJsonOrApiUrl(string url)
        {
            string lower = url.ToLowerInvariant();

            if (IsDefinitelyNotTextUrl(lower))
            {
                return false;
            }

            return lower.Contains("/api/")
                || lower.Contains("/api?")
                || lower.EndsWith(".json")
                || lower.Contains(".json?")
                || lower.Contains("/json/")
                || lower.EndsWith("/json")
                || lower.Contains("/config/")
                || lower.Contains("/streams/")
                || lower.Contains("/playlist/");
        }

        private bool IsDefinitelyNotTextUrl(string url)
        {
            string lower = url.ToLowerInvariant();

            return lower.EndsWith(".png")
                || lower.EndsWith(".jpg")
                || lower.EndsWith(".jpeg")
                || lower.EndsWith(".gif")
                || lower.EndsWith(".svg")
                || lower.EndsWith(".webp")
                || lower.EndsWith(".ico")
                || lower.EndsWith(".css")
                || lower.Contains("/image/")
                || lower.Contains("/images/")
                || lower.Contains("/assets/images/")
                || lower.Contains("recaptcha")
                || lower.Contains("googlesyndication")
                || lower.Contains("accuweather")
                || lower.Contains("33across")
                || lower.Contains("sonobi");
        }

        private string NormalizeUrl(string url, string baseUrl)
        {
            url = CleanUrl(url);

            if (url.StartsWith("//"))
            {
                var baseUri = new Uri(baseUrl);
                return baseUri.Scheme + ":" + url;
            }

            if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                return url;
            }

            var baseUriForRelative = new Uri(baseUrl);
            return new Uri(baseUriForRelative, url).ToString();
        }

        private string CleanUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            return url
                .Trim()
                .TrimEnd(',', ';', ')', ']', '}')
                .Replace("\\/", "/");
        }

        private async Task<string> DownloadTextAsync(string url)
        {
            using (var client = CreateHttpClient())
            {
                return await client.GetStringAsync(url);
            }
        }

        private async Task<string> DownloadTextSafeAsync(string url)
        {
            try
            {
                if (IsDefinitelyNotTextUrl(url))
                {
                    Log.Debug("Skipping non-text URL: {Url}", url);
                    return string.Empty;
                }

                return await DownloadTextAsync(url);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to download text from URL: {Url}", url);
                return string.Empty;
            }
        }

        private HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(HTTP_TIMEOUT)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 RadioApp/1.0");

            return client;
        }

        private List<string> ExtractIframeUrls(string html, string baseUrl)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(html))
                return result;

            var matches = Regex.Matches(
                html,
                @"<iframe[^>]+src=[""'](?<src>[^""']+)[""']",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in matches)
            {
                string src = match.Groups["src"].Value;

                try
                {
                    result.Add(NormalizeUrl(src, baseUrl));
                }
                catch
                {
                    // Ignore invalid iframe URL.
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        private bool IsSecureNetSystemsPlayerUrl(string url)
        {
            string lower = url.ToLowerInvariant();

            return lower.Contains("securenetsystems.net/v5/");
        }

        private List<string> SortCandidatesByPriority(List<string> candidates)
        {
            return candidates
                .OrderByDescending(GetCandidatePriority)
                .ThenBy(x => x.Length)
                .ToList();
        }

        private int GetCandidatePriority(string url)
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

        private bool IsDefinitelyNotStreamUrl(string url)
        {
            string lower = url.ToLowerInvariant();

            return lower.EndsWith(".png")
                || lower.EndsWith(".jpg")
                || lower.EndsWith(".jpeg")
                || lower.EndsWith(".gif")
                || lower.EndsWith(".svg")
                || lower.EndsWith(".webp")
                || lower.EndsWith(".css")
                || lower.EndsWith(".ico")
                || lower.Contains("/css/")
                || lower.Contains("/image/")
                || lower.Contains("/images/")
                || lower.Contains("/assets/images/")
                || lower.Contains("recaptcha")
                || lower.Contains("googlesyndication")
                || lower.Contains("googleapis.com/json")
                || lower.Contains("accuweather");
        }

        private async Task<DiscoveredRadioStream> TryDiscoverSecureNetDirectStreamAsync(
                                                    string pageUrl,
                                                    List<string> candidates)
        {
            foreach (string candidate in candidates)
            {
                string directStreamUrl = TryBuildSecureNetDirectStreamUrl(candidate);

                if (string.IsNullOrWhiteSpace(directStreamUrl))
                {
                    continue;
                }

                Log.Information(
                    "SecureNetSystems direct stream candidate built. PlayerUrl: {PlayerUrl}, DirectStreamUrl: {DirectStreamUrl}",
                    candidate,
                    directStreamUrl
                );

                bool isAudio = await _streamInfoService.IsAudioStreamAsync(directStreamUrl);

                if (isAudio)
                {
                    Log.Information("SecureNetSystems direct stream is playable: {StreamUrl}", directStreamUrl);

                    return await _streamInfoService.ReadStreamInfoAsync(pageUrl, directStreamUrl);
                }
            }

            return null;
        }

        private string TryBuildSecureNetDirectStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            var match = Regex.Match(
                url,
                @"https?://streamdb(?<number>\d+)web\.securenetsystems\.net/v5/(?<station>[A-Za-z0-9_-]+)",
                RegexOptions.IgnoreCase
            );

            if (!match.Success)
            {
                return string.Empty;
            }

            string number = match.Groups["number"].Value;
            string station = match.Groups["station"].Value;

            return "https://ice" + number + ".securenetsystems.net/" + station;
        }

        private bool IsRejectedStreamCandidate(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return true;
            }

            string lower = url.ToLowerInvariant();

            return lower.Contains("/programs/file/listen/")
                || lower.Contains("/file/listen/")
                || lower.Contains("/podcast/")
                || lower.Contains("/archive/")
                || lower.Contains("/news/")
                || lower.Contains("/advert")
                || lower.Contains("/ads/")
                || lower.Contains("/banner/")
                || lower.Contains("/episode/")
                || lower.Contains("/episodes/")
                || lower.Contains("/record/")
                || lower.Contains("/records/");
        }

        private async Task<DiscoveredRadioStream> TryDiscoverAmperwaveDirectStreamAsync(
                                                    string pageUrl,
                                                    List<string> candidates)
        {
            var checkedStreams = new List<DiscoveredRadioStream>();

            foreach (string candidate in candidates)
            {
                if (!IsAmperwaveManifestUrl(candidate))
                {
                    continue;
                }

                Log.Information("Amperwave manifest candidate found: {Candidate}", candidate);

                List<string> directCandidates = await BuildAmperwaveDirectStreamCandidatesAsync(candidate);

                foreach (string directCandidate in directCandidates)
                {
                    Log.Information("Checking Amperwave direct candidate: {StreamUrl}", directCandidate);

                    DiscoveredRadioStream streamInfo =
                        await _streamInfoService.GetStreamInfoIfPlayableAsync(pageUrl, directCandidate);

                    if (streamInfo == null)
                    {
                        continue;
                    }

                    checkedStreams.Add(streamInfo);

                    Log.Information(
                        "Amperwave candidate playable. StreamUrl: {StreamUrl}, Bitrate: {Bitrate}, StationName: {StationName}",
                        streamInfo.StreamUrl,
                        streamInfo.Bitrate.HasValue ? streamInfo.Bitrate.Value.ToString() : "unknown",
                        streamInfo.StationName
                    );

                    if (streamInfo.Bitrate.HasValue && streamInfo.Bitrate.Value >= 128)
                    {
                        Log.Information("Good Amperwave stream selected immediately: {StreamUrl}", streamInfo.StreamUrl);
                        return streamInfo;
                    }
                }
            }

            if (checkedStreams.Count == 0)
            {
                return null;
            }

            DiscoveredRadioStream bestStream = checkedStreams
                .OrderByDescending(GetStreamQualityScore)
                .First();

            if (bestStream.Bitrate.HasValue && bestStream.Bitrate.Value < 64)
            {
                Log.Warning(
                    "Only low-bitrate Amperwave streams were found. Selected StreamUrl: {StreamUrl}, Bitrate: {Bitrate}",
                    bestStream.StreamUrl,
                    bestStream.Bitrate.Value
                );
            }
            else
            {
                Log.Information(
                    "Best Amperwave stream selected. StreamUrl: {StreamUrl}, Bitrate: {Bitrate}",
                    bestStream.StreamUrl,
                    bestStream.Bitrate.HasValue ? bestStream.Bitrate.Value.ToString() : "unknown"
                );
            }

            return bestStream;
        }

        private bool IsAmperwaveManifestUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string lower = url.ToLowerInvariant();

            return lower.Contains("live.amperwave.net/manifest/")
                || lower.Contains("amperwave.net/manifest/");
        }

        private async Task<string> GetFinalUrlAsync(string url)
        {
            using (var client = CreateHttpClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);

                using (var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    if (response.RequestMessage != null &&
                        response.RequestMessage.RequestUri != null)
                    {
                        return response.RequestMessage.RequestUri.ToString();
                    }

                    return url;
                }
            }
        }

        private bool IsRejectedFinalStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return true;
            }

            string lower = url.ToLowerInvariant();

            return lower.Contains("live.amperwave.net/manifest/")
                || lower.Contains("/manifest/")
                || lower.Contains("fmaac-ibc")
                || lower.Contains("aac-ibc")
                || lower.Contains("aac-hlsc")
                || lower.EndsWith(".m3u")
                || lower.EndsWith(".pls");
        }

        private int GetStreamQualityScore(DiscoveredRadioStream streamInfo)
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

        private async Task<List<string>> BuildAmperwaveDirectStreamCandidatesAsync(string manifestUrl)
        {
            var result = new List<string>();

            try
            {
                string finalUrl = await GetFinalUrlAsync(manifestUrl);

                if (string.IsNullOrWhiteSpace(finalUrl))
                {
                    return result;
                }

                var finalUri = new Uri(finalUrl);
                string streamName = finalUri.AbsolutePath.Trim('/');

                if (string.IsNullOrWhiteSpace(streamName))
                {
                    return result;
                }

                foreach (string candidateName in BuildAmperwaveStreamNameVariants(streamName))
                {
                    string candidateUrl = finalUri.Scheme + "://" + finalUri.Host + "/" + candidateName;
                    result.Add(candidateUrl);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to build Amperwave direct stream candidates from manifest: {ManifestUrl}", manifestUrl);
            }

            return result
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> BuildAmperwaveStreamNameVariants(string streamName)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(streamName))
            {
                return result;
            }

            // Сначала пробуем варианты, которые чаще всего являются прямыми потоками.
            if (streamName.Contains("aac-ibc"))
            {
                result.Add(streamName.Replace("aac-ibc", "mp3-ibc"));
                result.Add(streamName);
            }
            else if (streamName.Contains("aac-hlsc"))
            {
                result.Add(streamName.Replace("aac-hlsc", "mp3-ibc"));
                result.Add(streamName.Replace("aac-hlsc", "aac-ibc"));
                result.Add(streamName);
            }
            else if (streamName.Contains("aac"))
            {
                result.Add(streamName.Replace("aac", "mp3"));
                result.Add(streamName);
            }
            else if (streamName.Contains("mp3"))
            {
                result.Add(streamName);
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }


        private async Task<DiscoveredRadioStream> TryDiscoverSecureNetFromCallSignAsync(
                                                    string pageUrl,
                                                    string html)
        {
            if (!IsRadioStationUsaPage(pageUrl))
            {
                return null;
            }

            string callSign = ExtractRadioStationUsaCallSign(html);

            if (string.IsNullOrWhiteSpace(callSign))
            {
                Log.Information("SecureNetSystems fallback skipped. Call sign was not found.");
                return null;
            }

            Log.Information("Trying SecureNetSystems fallback from call sign: {CallSign}", callSign);

            var candidates = BuildSecureNetCandidatesFromCallSign(callSign);

            foreach (var batch in Batch(candidates, SecureNetFallbackBatchSize))
            {
                var tasks = batch
                    .Select(candidate => CheckSecureNetCallSignCandidateAsync(pageUrl, candidate, callSign))
                    .ToList();

                DiscoveredRadioStream[] results = await Task.WhenAll(tasks);

                DiscoveredRadioStream firstPlayable = results.FirstOrDefault(x => x != null);

                if (firstPlayable != null)
                {
                    Log.Information(
                        "SecureNetSystems fallback found playable stream. CallSign: {CallSign}, StreamUrl: {StreamUrl}",
                        callSign,
                        firstPlayable.StreamUrl
                    );

                    return firstPlayable;
                }
            }

            Log.Information("SecureNetSystems fallback did not find a playable stream for call sign: {CallSign}", callSign);

            return null;
        }

        private async Task<DiscoveredRadioStream> CheckSecureNetCallSignCandidateAsync(
    string pageUrl,
    string candidate,
    string callSign)
        {
            Log.Debug("Checking SecureNetSystems call sign candidate: {Candidate}", candidate);

            DiscoveredRadioStream streamInfo =
                await _streamInfoService.GetStreamInfoIfPlayableAsync(
                    pageUrl,
                    candidate,
                    SecureNetFallbackProbeTimeoutSeconds
                );

            if (streamInfo == null)
            {
                return null;
            }

            if (IsBadProviderStationName(streamInfo.StationName))
            {
                streamInfo.StationName = callSign;
            }

            if (IsBadProviderStationName(streamInfo.Description))
            {
                streamInfo.Description = string.Empty;
            }

            return streamInfo;
        }

        private List<string> BuildSecureNetCandidatesFromCallSign(string callSign)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(callSign))
            {
                return result;
            }

            string normalizedCallSign = NormalizeCallSign(callSign);

            var stationIds = new List<string>{
                                                normalizedCallSign,
                                                normalizedCallSign + "FM",
                                                normalizedCallSign + "AM"
                                             };

            foreach (string stationId in stationIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                for (int i = 1; i <= SecureNetFallbackMaxIceServerNumber; i++)
                {
                    result.Add("https://ice" + i + ".securenetsystems.net/" + stationId);
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private IEnumerable<List<string>> Batch(List<string> source, int batchSize)
        {
            for (int i = 0; i < source.Count; i += batchSize)
            {
                yield return source.Skip(i).Take(batchSize).ToList();
            }
        }

        private bool IsRadioStationUsaPage(string pageUrl)
        {
            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            return uri.Host.IndexOf("radiostationusa.fm", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsBadProviderStationName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            string lower = value.ToLowerInvariant();

            return lower.Contains("securenet systems")
                || lower.Contains("cirrus")
                || lower.Contains("streaming by")
                || lower.Contains("icecast")
                || lower.Contains("shoutcast");
        }

        private string ExtractRadioStationUsaCallSign(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            string plainText = StripHtml(html);

            var match = Regex.Match(
                plainText,
                @"Call\s*sign\s*:?\s*(?<call>[A-Z0-9\-]{3,10})",
                RegexOptions.IgnoreCase
            );

            if (match.Success)
            {
                return NormalizeCallSign(match.Groups["call"].Value);
            }

            match = Regex.Match(
                plainText,
                @"\b[A-Z]{3,5}(?:-FM|-AM)?\b",
                RegexOptions.IgnoreCase
            );

            if (match.Success)
            {
                return NormalizeCallSign(match.Value);
            }

            return string.Empty;
        }

        private string StripHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            string withoutScripts = Regex.Replace(
                html,
                @"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>",
                " ",
                RegexOptions.IgnoreCase
            );

            string withoutStyles = Regex.Replace(
                withoutScripts,
                @"<style\b[^<]*(?:(?!<\/style>)<[^<]*)*<\/style>",
                " ",
                RegexOptions.IgnoreCase
            );

            string plainText = Regex.Replace(
                withoutStyles,
                "<.*?>",
                " ",
                RegexOptions.IgnoreCase
            );

            return Regex.Replace(plainText, @"\s+", " ").Trim();
        }

        private string NormalizeCallSign(string callSign)
        {
            if (string.IsNullOrWhiteSpace(callSign))
            {
                return string.Empty;
            }

            return callSign
                .Trim()
                .ToUpperInvariant()
                .Replace("-FM", "")
                .Replace("-AM", "")
                .Replace("-", "");
        }

        private StreamCandidateEvaluation EvaluateStreamCandidateUrl(string url)
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

            if (IsDefinitelyNotStreamUrl(url) || IsRejectedStreamCandidate(url))
            {
                result.Score = -1000;
                result.Reason = "Rejected by non-stream filter";
                return result;
            }

            Uri uri;

            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
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

        private int TryExtractBitrateFromUrl(string url)
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

        private async Task<DiscoveredRadioStream> TryDiscoverBestGenericCandidateAsync(
    string pageUrl,
    List<string> candidates,
    string html)
        {
            var evaluatedCandidates = candidates
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(x => !IsRejectedStreamCandidate(x))
                .Where(x => !IsRejectedFinalStreamUrl(x))
                .Where(x => !IsDefinitelyNotStreamUrl(x))
                .Select(EvaluateStreamCandidateUrl)
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

                    if (IsRejectedStreamCandidate(streamInfo.StreamUrl) ||
                        IsRejectedFinalStreamUrl(streamInfo.StreamUrl) ||
                        IsDefinitelyNotStreamUrl(streamInfo.StreamUrl))
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
                        "HTTP-confirmed stream candidate. Score: {Score}, Url: {Url}, Bitrate: {Bitrate}",
                        candidate.Score,
                        streamInfo.StreamUrl,
                        streamInfo.Bitrate.HasValue ? streamInfo.Bitrate.Value.ToString() : "unknown"
                    );
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
                DiscoveredRadioStream confirmedStream = confirmed.StreamInfo;

                string pageTitle = ExtractPageTitle(html);

                if (!string.IsNullOrWhiteSpace(pageTitle))
                {
                    confirmedStream.StationName = pageTitle;
                }

                if (string.IsNullOrWhiteSpace(confirmedStream.StationName))
                {
                    confirmedStream.StationName = BuildFallbackStationName(pageUrl, confirmedStream.StreamUrl);
                }

                if (string.IsNullOrWhiteSpace(confirmedStream.Description))
                {
                    confirmedStream.Description = BuildTopCandidateDescription(evaluatedCandidates);
                }

                Log.Information(
                    "Best HTTP-confirmed generic stream selected. StreamUrl: {StreamUrl}, StationName: {StationName}",
                    confirmedStream.StreamUrl,
                    confirmedStream.StationName
                );

                return confirmedStream;
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

        private string BuildTopCandidateDescription(List<StreamCandidateEvaluation> candidates)
        {
            var top = candidates
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(3)
                .Select((x, index) =>
                    (index + 1) + ". " +
                    (x.Bitrate.HasValue ? x.Bitrate.Value + " kbps" : "unknown bitrate") +
                    " | score " + x.Score +
                    " | " + x.Url
                );

            return "Detected stream candidates:\r\n" + string.Join("\r\n", top);
        }

        private List<string> ExtractStreamUrlsFromStructuredText(string text, string baseUrl)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            var patterns = new[]
            {
                @"(?:stream|streamUrl|stream_url|streamURL|audio|audioUrl|audio_url|src|url|source)\s*[:=]\s*[""'](?<url>[^""']+)[""']",
                @"data-(?:stream|url|src|audio|audio-url|stream-url)=[""'](?<url>[^""']+)[""']",
                @"""(?:stream|streamUrl|stream_url|audio|audioUrl|audio_url|src|url|source)""\s*:\s*""(?<url>[^""]+)"""
            };

            foreach (string pattern in patterns)
            {
                var matches = Regex.Matches(
                    text,
                    pattern,
                    RegexOptions.IgnoreCase
                );

                foreach (Match match in matches)
                {
                    string rawUrl = match.Groups["url"].Value;

                    if (string.IsNullOrWhiteSpace(rawUrl))
                    {
                        continue;
                    }

                    rawUrl = DecodeJavaScriptString(rawUrl);

                    try
                    {
                        string normalizedUrl = NormalizeUrl(rawUrl, baseUrl);

                        if (IsPossibleStreamUrl(normalizedUrl) &&
                            !IsRejectedStreamCandidate(normalizedUrl) &&
                            !IsDefinitelyNotStreamUrl(normalizedUrl))
                        {
                            result.Add(normalizedUrl);
                        }
                    }
                    catch
                    {
                        // Ignore invalid URL.
                    }
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string DecodeJavaScriptString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\/", "/")
                .Replace("\\u002F", "/")
                .Replace("\\u002f", "/")
                .Replace("&amp;", "&");
        }

        private string BuildFallbackStationName(string pageUrl, string streamUrl)
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

        private string ExtractNameFromPageUrl(string pageUrl)
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

        private string ExtractNameFromStreamUrl(string streamUrl)
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

        private string ToDisplayName(string raw)
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

        private string ExtractPageTitle(string html)
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

        private string CleanPageTitle(string title)
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
    }
}