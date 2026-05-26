using RadioApp.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RadioApp.Services.StreamDiscovery
{
    internal class AmperwaveDiscoveryService
    {
        private readonly HttpTextDownloadService _httpTextDownloadService;
        private readonly RadioStreamInfoService _streamInfoService;
        private readonly StreamCandidatePrioritizer _prioritizer = new StreamCandidatePrioritizer();

        public AmperwaveDiscoveryService()
        {
            _httpTextDownloadService = new HttpTextDownloadService();
            _streamInfoService = new RadioStreamInfoService();
            _prioritizer = new StreamCandidatePrioritizer();
        }

        public List<string> BuildAmperwaveStreamNameVariants(string streamName)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(streamName))
            {
                return result;
            }

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
        public async Task<List<string>> BuildAmperwaveDirectStreamCandidatesAsync(string manifestUrl)
        {
            var result = new List<string>();

            try
            {
                string finalUrl = await _httpTextDownloadService.GetFinalUrlAsync(manifestUrl);

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
        public bool IsAmperwaveManifestUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string lower = url.ToLowerInvariant();

            return lower.Contains("live.amperwave.net/manifest/")
                || lower.Contains("amperwave.net/manifest/");
        }
        public async Task<DiscoveredRadioStream> TryDiscoverAmperwaveDirectStreamAsync(
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
                .OrderByDescending(_prioritizer.GetStreamQualityScore)
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
    }
}
