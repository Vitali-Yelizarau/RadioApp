using RadioApp.Models;
using RadioApp.Services.StreamDiscovery;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadioApp.Services
{
    public class RadioStreamDiscoveryService
    {
        private readonly StreamCandidateCollector _streamCandidateCollector;
        private readonly SecureNetSystemsDiscoveryService _secureNetSystemsDiscoveryService;
        private readonly GenericStreamDiscoveryService _genericStreamDiscoveryService;
        private readonly InternetRadioDirectoryDiscoveryService _internetRadioDirectoryDiscoveryService;
        private readonly HearMeFmDiscoveryService _hearMeFmDiscoveryService;

        public RadioStreamDiscoveryService()
        {
            _streamCandidateCollector = new StreamCandidateCollector();
            _secureNetSystemsDiscoveryService = new SecureNetSystemsDiscoveryService();
            _genericStreamDiscoveryService = new GenericStreamDiscoveryService();
            _internetRadioDirectoryDiscoveryService = new InternetRadioDirectoryDiscoveryService();
            _hearMeFmDiscoveryService = new HearMeFmDiscoveryService(
                new HttpTextDownloadService(),
                new RadioStreamInfoService()
            );
        }

        public async Task<DiscoveredRadioStream> DiscoverAsync(
            string pageUrl,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pageUrl))
            {
                throw new ArgumentException("Page URL is empty.", nameof(pageUrl));
            }

            Log.Information(
                "Stream discovery started. PageUrl: {PageUrl}",
                pageUrl
            );

            cancellationToken.ThrowIfCancellationRequested();

            string htmlForFallback = await _streamCandidateCollector.DownloadPageHtmlSafeAsync(
                pageUrl,
                cancellationToken
            );

            /*
             * IMPORTANT:
             * Direct Internet-Radio.com station pages must be handled by the dedicated
             * InternetRadioDirectoryDiscoveryService BEFORE generic discovery.
             *
             * Otherwise generic discovery can pick some unrelated station/proxy link
             * from the same directory page, like:
             * https://uk2.internet-radio.com/proxy/belters?mp=/stream
             */
            if (_internetRadioDirectoryDiscoveryService.IsDirectInternetRadioStationPage(pageUrl))
            {
                DiscoveredRadioStream directInternetRadioResult = await _internetRadioDirectoryDiscoveryService.TryDiscoverAsync(
                    pageUrl,
                    htmlForFallback,
                    cancellationToken
                );

                if (directInternetRadioResult != null)
                {
                    return directInternetRadioResult;
                }

                Log.Information(
                    "Direct Internet-Radio.com fallback did not find a stream. Continuing with generic discovery. PageUrl: {PageUrl}",
                    pageUrl
                );
            }

            DiscoveredRadioStream hearMeResult = await _hearMeFmDiscoveryService.TryDiscoverAsync(
                pageUrl,
                cancellationToken
            );

            if (hearMeResult != null)
            {
                Log.Information(
                    "HearMe.fm stream discovered. PageUrl: {PageUrl}, StreamUrl: {StreamUrl}",
                    pageUrl,
                    hearMeResult.StreamUrl
                );

                return hearMeResult;
            }

            List<string> candidates = await _streamCandidateCollector.CollectCandidateStreamUrlsAsync(
                pageUrl,
                cancellationToken
            );

            foreach (string candidate in BuildListenSubdomainCandidates(pageUrl))
            {
                if (!candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    Log.Information(
                        "Listen-subdomain fallback candidate added. Candidate: {Candidate}",
                        candidate
                    );

                    candidates.Add(candidate);
                }
            }

            Log.Information(
                "Collected {Count} candidate stream URLs for PageUrl: {PageUrl}",
                candidates.Count,
                pageUrl
            );

            bool hasSecureNetEvidence =
                _secureNetSystemsDiscoveryService.HasSecureNetEvidence(
                    htmlForFallback,
                    candidates
                );

            if (hasSecureNetEvidence)
            {
                DiscoveredRadioStream secureNetByCallSignResult =
                    await _secureNetSystemsDiscoveryService.TryDiscoverSecureNetFromCallSignAsync(
                        pageUrl,
                        htmlForFallback,
                        cancellationToken
                    );

                if (secureNetByCallSignResult != null)
                {
                    return secureNetByCallSignResult;
                }
            }
            else
            {
                Log.Information(
                    "SecureNetSystems call sign fallback skipped because no SecureNet evidence was found. PageUrl: {PageUrl}",
                    pageUrl
                );
            }

            DiscoveredRadioStream genericResult =
                await _genericStreamDiscoveryService.TryDiscoverBestGenericCandidateAsync(
                    pageUrl,
                    candidates,
                    htmlForFallback
                );

            if (genericResult != null)
            {
                return genericResult;
            }

            /*
             * This fallback is still useful for pages like:
             * https://www.radio.net/s/bloodstream
             *
             * There the original page is not Internet-Radio.com, but the station slug
             * can be used to check:
             * https://www.internet-radio.com/station/bloodstream/
             */
            if (_internetRadioDirectoryDiscoveryService.CanUseAsFallbackFor(pageUrl))
            {
                DiscoveredRadioStream internetRadioDirectoryResult =
                    await _internetRadioDirectoryDiscoveryService.TryDiscoverAsync(
                        pageUrl,
                        htmlForFallback,
                        cancellationToken
                    );

                if (internetRadioDirectoryResult != null)
                {
                    return internetRadioDirectoryResult;
                }
            }
            else
            {
                Log.Information(
                    "Internet-Radio.com fallback skipped for unsupported source page. PageUrl: {PageUrl}",
                    pageUrl
                );
            }

            throw new InvalidOperationException("Could not find a playable stream URL on this page.");
        }

        private static IEnumerable<string> BuildListenSubdomainCandidates(string pageUrl)
        {
            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri))
                yield break;

            var host = pageUri.Host;

            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                host = host.Substring(4);

            yield return $"https://listen.{host}/";
            yield return $"http://listen.{host}/";
        }
    }
}