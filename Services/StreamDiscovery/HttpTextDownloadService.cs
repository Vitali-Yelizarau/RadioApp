using Serilog;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RadioApp.Services
{
    public class HttpTextDownloadService
    {
        private const int HttpTimeoutSeconds = 15;
        private readonly StreamCandidateFilter _filter;

        public HttpTextDownloadService()
        {
            _filter = new StreamCandidateFilter();
        }

        public async Task<string> DownloadTextAsync(string url)
        {
            using (var client = CreateHttpClient())
            {
                return await client.GetStringAsync(url);
            }
        }


        public async Task<string> DownloadTextAsync(string url, CancellationToken cancellationToken)
        {
            using (var client = CreateHttpClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);

                using (var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken))
                {
                    response.EnsureSuccessStatusCode();

                    return await response.Content.ReadAsStringAsync();
                }
            }
        }

        public async Task<string> DownloadTextSafeAsync(
                                        string url,
                                        CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_filter.IsDefinitelyNotTextUrl(url))
                {
                    Log.Debug("Skipping non-text URL: {Url}", url);
                    return string.Empty;
                }

                return await DownloadTextAsync(url, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Log.Information("Text download cancelled by user timeout. Url: {Url}", url);
                    throw;
                }

                Log.Debug(
                    ex,
                    "Text download timed out or was cancelled internally. Url will be skipped. Url: {Url}",
                    url
                );

                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to download text from URL: {Url}", url);
                return string.Empty;
            }
        }

        public async Task<string> GetFinalUrlAsync(string url)
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

        private HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds)
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 RadioApp/1.0");

            return client;
        }
    }
}