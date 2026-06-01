using RadioApp.Models;
using RadioApp.Services.StreamDiscovery;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace RadioApp.Services
{
    public class RadioStreamDiscoveryService
    {
        private readonly StreamCandidateCollector _streamCandidateCollector;
        private readonly StreamCandidatePrioritizer _streamCandidatePrioritizer;
        private readonly HttpTextDownloadService _httpTextDownloadService;
        private readonly SecureNetSystemsDiscoveryService _secureNetSystemsDiscoveryService;
        private readonly AmperwaveDiscoveryService _amperwaveDiscoveryService;
        private readonly GenericStreamDiscoveryService _genericStreamDiscoveryService;


        public RadioStreamDiscoveryService()
        {
            _streamCandidateCollector = new StreamCandidateCollector();
            _streamCandidatePrioritizer = new StreamCandidatePrioritizer();
            _httpTextDownloadService = new HttpTextDownloadService();
            _secureNetSystemsDiscoveryService = new SecureNetSystemsDiscoveryService();
            _amperwaveDiscoveryService = new AmperwaveDiscoveryService();
            _genericStreamDiscoveryService = new GenericStreamDiscoveryService();
        }

        public async Task<DiscoveredRadioStream> DiscoverAsync(string pageUrl)
        {
            Log.Information("Stream discovery started. PageUrl: {PageUrl}", pageUrl);

            var candidates = await _streamCandidateCollector.CollectCandidateStreamUrlsAsync(pageUrl);

            Log.Information(
                "Collected {Count} candidate stream URLs for PageUrl: {PageUrl}",
                candidates.Count,
                pageUrl
            );

            candidates = _streamCandidatePrioritizer.SortCandidatesByPriority(candidates);

            string htmlForFallback = await _httpTextDownloadService.DownloadTextSafeAsync(pageUrl);

            DiscoveredRadioStream secureNetResult =
                await _secureNetSystemsDiscoveryService.TryDiscoverSecureNetDirectStreamAsync(pageUrl, candidates);

            if (secureNetResult != null)
            {
                return secureNetResult;
            }

            DiscoveredRadioStream amperwaveResult =
                await _amperwaveDiscoveryService.TryDiscoverAmperwaveDirectStreamAsync(pageUrl, candidates);

            if (amperwaveResult != null)
            {
                return amperwaveResult;
            }

            DiscoveredRadioStream secureNetByCallSignResult =
                await _secureNetSystemsDiscoveryService.TryDiscoverSecureNetFromCallSignAsync(pageUrl, htmlForFallback);

            if (secureNetByCallSignResult != null)
            {
                return secureNetByCallSignResult;
            }

            DiscoveredRadioStream genericResult =
                await _genericStreamDiscoveryService.TryDiscoverBestGenericCandidateAsync(pageUrl, candidates, htmlForFallback);

            if (genericResult != null)
            {
                return genericResult;
            }

            throw new InvalidOperationException("Could not find a playable stream URL on this page.");
        }
    }
}