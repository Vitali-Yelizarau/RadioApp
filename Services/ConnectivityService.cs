using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RadioApp.Services
{
    /// <summary>
    /// Actively probes general internet reachability by hitting several well-known,
    /// lightweight connectivity-check endpoints concurrently. If ANY of them responds
    /// successfully, the internet is considered available.
    ///
    /// Active probing is used deliberately instead of relying on LibVLC playback
    /// events: when the network drops, VLC frequently just stalls silently without
    /// raising <c>EncounteredError</c>, so the player alone is not a reliable signal
    /// for "are we online".
    /// </summary>
    public class ConnectivityService
    {
        // Endpoints purpose-built for captive-portal / connectivity checks. They
        // return tiny responses (mostly HTTP 204 No Content), so each probe is cheap.
        // Plain HTTP is used intentionally: it avoids TLS negotiation cost and the
        // 204 endpoints are designed to be hit over HTTP for exactly this purpose.
        private static readonly string[] ProbeUrls =
        {
            "http://www.gstatic.com/generate_204",            // Google
            "http://cp.cloudflare.com/generate_204",          // Cloudflare
            "http://www.msftconnecttest.com/connecttest.txt"  // Microsoft
        };

        // One shared client. No global timeout — each probe is bounded by its own
        // CancellationTokenSource so a single slow host can't hold up the whole check.
        private static readonly HttpClient SharedHttpClient = CreateHttpClient();

        private readonly TimeSpan _perProbeTimeout = TimeSpan.FromSeconds(4);

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            client.DefaultRequestHeaders.ConnectionClose = true;

            return client;
        }

        /// <summary>
        /// Returns <c>true</c> as soon as any probe succeeds; returns <c>false</c>
        /// only if every probe fails or times out. Never throws.
        /// </summary>
        public async Task<bool> CheckInternetAsync()
        {
            var tasks = new List<Task<bool>>();

            foreach (var url in ProbeUrls)
            {
                tasks.Add(ProbeAsync(url));
            }

            while (tasks.Count > 0)
            {
                Task<bool> finished = await Task.WhenAny(tasks).ConfigureAwait(false);
                tasks.Remove(finished);

                try
                {
                    if (finished.Result)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Ignore this probe's failure and let the others race.
                }
            }

            return false;
        }

        private async Task<bool> ProbeAsync(string url)
        {
            using (var cts = new CancellationTokenSource(_perProbeTimeout))
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    using (var response = await SharedHttpClient
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                        .ConfigureAwait(false))
                    {
                        // 2xx, including 204 No Content from the generate_204 endpoints.
                        return response.IsSuccessStatusCode;
                    }
                }
                catch
                {
                    // DNS failure, timeout, connection refused, etc. → this host is
                    // unreachable; the caller treats "all unreachable" as offline.
                    return false;
                }
            }
        }
    }
}