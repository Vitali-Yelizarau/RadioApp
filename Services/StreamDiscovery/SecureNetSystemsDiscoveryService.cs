using RadioApp.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RadioApp.Services.StreamDiscovery
{
    internal class SecureNetSystemsDiscoveryService
    {
        private const int SecureNetFallbackMaxIceServerNumber = 99;
        private const int SecureNetFallbackProbeTimeoutSeconds = 2;
        private const int SecureNetFallbackBatchSize = 20;

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
        public async Task<DiscoveredRadioStream> CheckSecureNetCallSignCandidateAsync(
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
        public async Task<DiscoveredRadioStream> TryDiscoverSecureNetFromCallSignAsync(
                                                    string pageUrl,
                                                    string html)
        {
            if (!_radioStationUsaParser.IsRadioStationUsaPage(pageUrl))
            {
                return null;
            }

            string callSign = _radioStationUsaParser.ExtractRadioStationUsaCallSign(html);

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
    }
}
