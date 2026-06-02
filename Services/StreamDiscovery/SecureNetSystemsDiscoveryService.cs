using RadioApp.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RadioApp.Services.StreamDiscovery
{
    internal class SecureNetSystemsDiscoveryService
    {
        private const int SecureNetFallbackMaxIceServerNumber = 99;
        private const int SecureNetFallbackProbeTimeoutSeconds = 6;
        private const int SecureNetFallbackBatchSize = 5;

        private readonly RadioStationUsaParser _radioStationUsaParser;
        private readonly RadioStreamInfoService _streamInfoService;

        public SecureNetSystemsDiscoveryService()
        {
            _radioStationUsaParser = new RadioStationUsaParser();
            _streamInfoService = new RadioStreamInfoService();
        }

        public IEnumerable<List<string>> Batch(List<string> source, int batchSize)
        {
            for (int i = 0; i < source.Count; i += batchSize)
            {
                yield return source.Skip(i).Take(batchSize).ToList();
            }
        }
        public bool IsBadProviderStationName(string value)
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
        public List<string> BuildSecureNetCandidatesFromCallSign(string callSign)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(callSign))
            {
                return result;
            }

            string normalizedCallSign = _radioStationUsaParser.NormalizeCallSign(callSign);

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
        private async Task<DiscoveredRadioStream> CheckSecureNetCallSignCandidateAsync(
                                                    string pageUrl,
                                                    string streamUrl,
                                                    string callSign)
        {
            Log.Debug(
                "Checking SecureNetSystems call sign candidate: {StreamUrl}",
                streamUrl
            );

            try
            {
                using (var client = CreateSecureNetProbeHttpClient())
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
                    request.Headers.Add("Icy-MetaData", "1");

                    using (var response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            Log.Debug(
                                "SecureNetSystems candidate returned non-success status. StreamUrl: {StreamUrl}, StatusCode: {StatusCode}",
                                streamUrl,
                                response.StatusCode
                            );

                            return null;
                        }

                        string contentType = response.Content.Headers.ContentType != null
                            ? response.Content.Headers.ContentType.MediaType
                            : string.Empty;

                        string icyName = GetHeaderValue(response, "icy-name");
                        string icyDescription = GetHeaderValue(response, "icy-description");
                        string icyGenre = GetHeaderValue(response, "icy-genre");
                        string icyBr = GetHeaderValue(response, "icy-br");

                        bool looksLikeAudio =
                            !string.IsNullOrWhiteSpace(contentType) &&
                            contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);

                        bool hasIcyHeaders =
                            !string.IsNullOrWhiteSpace(icyName) ||
                            !string.IsNullOrWhiteSpace(icyDescription) ||
                            !string.IsNullOrWhiteSpace(icyBr);

                        if (!looksLikeAudio && !hasIcyHeaders)
                        {
                            Log.Debug(
                                "SecureNetSystems candidate does not look like audio. StreamUrl: {StreamUrl}, ContentType: {ContentType}",
                                streamUrl,
                                contentType
                            );

                            return null;
                        }

                        int? bitrate = TryParseBitrate(icyBr);

                        string stationName = callSign;

                        if (!string.IsNullOrWhiteSpace(icyName) &&
                            !IsBadProviderStationName(icyName))
                        {
                            stationName = icyName;
                        }

                        string description = string.Empty;

                        if (!string.IsNullOrWhiteSpace(icyDescription) &&
                            !IsBadProviderStationName(icyDescription))
                        {
                            description = icyDescription;
                        }
                        else
                        {
                            description = "SecureNetSystems stream";
                        }

                        Log.Information(
                            "SecureNetSystems stream found. StreamUrl: {StreamUrl}, ContentType: {ContentType}, Bitrate: {Bitrate}, StationName: {StationName}",
                            streamUrl,
                            contentType,
                            bitrate.HasValue ? bitrate.Value.ToString() : "unknown",
                            stationName
                        );

                        return new DiscoveredRadioStream
                        {
                            PageUrl = pageUrl,
                            StreamUrl = streamUrl,
                            StationName = stationName,
                            Description = description,
                            Genre = icyGenre ?? string.Empty,
                            Bitrate = bitrate
                        };
                    }
                }
            }
            catch (TaskCanceledException ex)
            {
                Log.Debug(
                    ex,
                    "SecureNetSystems candidate timed out. StreamUrl: {StreamUrl}, TimeoutSeconds: {TimeoutSeconds}",
                    streamUrl,
                    SecureNetFallbackProbeTimeoutSeconds
                );

                return null;
            }
            catch (Exception ex)
            {
                Log.Debug(
                    ex,
                    "SecureNetSystems candidate check failed. StreamUrl: {StreamUrl}",
                    streamUrl
                );

                return null;
            }
        }
        public async Task<DiscoveredRadioStream> TryDiscoverSecureNetFromCallSignAsync(
                                                    string pageUrl,
                                                    string html,
                                                    CancellationToken cancellationToken)
        {
            string callSign = _radioStationUsaParser.ExtractRadioStationUsaCallSign(html);

            if (string.IsNullOrWhiteSpace(callSign))
            {
                return null;
            }

            callSign = _radioStationUsaParser.NormalizeCallSign(callSign);

            if (!IsPlausibleCallSign(callSign))
            {
                Log.Information(
                    "SecureNetSystems fallback skipped because extracted call sign is not plausible. CallSign: {CallSign}",
                    callSign
                );

                return null;
            }

            Log.Information(
                "Trying SecureNetSystems fallback from call sign: {CallSign}",
                callSign
            );

            cancellationToken.ThrowIfCancellationRequested();

            List<string> candidates = BuildSecureNetCandidatesFromCallSign(callSign);

            for (int i = 0; i < candidates.Count; i += SecureNetFallbackBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<string> batch = candidates
                    .Skip(i)
                    .Take(SecureNetFallbackBatchSize)
                    .ToList();

                Task<DiscoveredRadioStream>[] tasks = batch
                    .Select(candidate => CheckSecureNetCallSignCandidateAsync(pageUrl, candidate, callSign))
                    .ToArray();

                DiscoveredRadioStream[] results = await Task.WhenAll(tasks);

                DiscoveredRadioStream found = results.FirstOrDefault(x => x != null);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        public string TryBuildSecureNetDirectStreamUrl(string url)
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
        public async Task<DiscoveredRadioStream> TryDiscoverSecureNetDirectStreamAsync(
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
        public bool IsSecureNetSystemsPlayerUrl(string url)
        {
            string lower = url.ToLowerInvariant();

            return lower.Contains("securenetsystems.net/v5/");
        }

        private HttpClient CreateSecureNetProbeHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(SecureNetFallbackProbeTimeoutSeconds)
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) RadioApp/1.0"
            );

            client.DefaultRequestHeaders.Accept.ParseAdd("*/*");

            return client;
        }

        private string GetHeaderValue(HttpResponseMessage response, string headerName)
        {
            if (response == null || string.IsNullOrWhiteSpace(headerName))
            {
                return string.Empty;
            }

            if (response.Headers.TryGetValues(headerName, out var responseHeaderValues))
            {
                return responseHeaderValues.FirstOrDefault() ?? string.Empty;
            }

            if (response.Content != null &&
                response.Content.Headers.TryGetValues(headerName, out var contentHeaderValues))
            {
                return contentHeaderValues.FirstOrDefault() ?? string.Empty;
            }

            return string.Empty;
        }

        public bool HasSecureNetEvidence(string html, IEnumerable<string> candidates)
        {
            if (!string.IsNullOrWhiteSpace(html))
            {
                string lowerHtml = html.ToLowerInvariant();

                if (lowerHtml.Contains("securenetsystems") ||
                    lowerHtml.Contains("secure net systems") ||
                    lowerHtml.Contains("cirrus"))
                {
                    return true;
                }
            }

            if (candidates != null)
            {
                foreach (string candidate in candidates)
                {
                    if (string.IsNullOrWhiteSpace(candidate))
                    {
                        continue;
                    }

                    string lowerCandidate = candidate.ToLowerInvariant();

                    if (lowerCandidate.Contains("securenetsystems.net") ||
                        lowerCandidate.Contains("ice") && lowerCandidate.Contains("securenetsystems"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool IsPlausibleCallSign(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim().ToUpperInvariant();

            string[] badValues =
            {
                "RADIO",
                "LIVE",
                "STREAM",
                "ONLINE",
                "PLAYER",
                "MUSIC",
                "FM",
                "AM",
                "WEBRADIO",
                "LISTEN"
            };

            if (badValues.Contains(normalized))
            {
                return false;
            }

            if (normalized.Length < 3 || normalized.Length > 7)
            {
                return false;
            }

            return normalized.All(char.IsLetterOrDigit);
        }

        private int? TryParseBitrate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (int.TryParse(value.Trim(), out int bitrate))
            {
                return bitrate;
            }

            return null;
        }
    }
}
