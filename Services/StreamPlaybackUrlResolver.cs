using Serilog;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace RadioApp.Services
{
    public class StreamPlaybackUrlResolver
    {
        private const int TimeoutSeconds = 5;

        public async Task<string> ResolvePlaybackUrlAsync(string streamUrl)
        {
            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                throw new ArgumentException("Stream URL is empty.", nameof(streamUrl));
            }

            string redirectUrl = await TryGetSingleRedirectLocationAsync(streamUrl);

            if (string.IsNullOrWhiteSpace(redirectUrl))
            {
                Log.Information(
                    "Playback URL does not require redirect resolving. Url: {Url}",
                    streamUrl
                );

                return streamUrl;
            }

            Log.Information(
                "Playback redirect resolved. From: {FromUrl}, To: {ToUrl}",
                streamUrl,
                redirectUrl
            );

            return redirectUrl;
        }

        private async Task<string> TryGetSingleRedirectLocationAsync(string url)
        {
            try
            {
                using (var client = CreateHttpClient())
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);

                    using (var response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (!IsRedirectStatusCode(response.StatusCode))
                        {
                            return string.Empty;
                        }

                        if (response.Headers.Location == null)
                        {
                            return string.Empty;
                        }

                        return BuildAbsoluteRedirectUrl(url, response.Headers.Location);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(
                    ex,
                    "Could not resolve first redirect URL. Original URL will be used. Url: {Url}",
                    url
                );

                return string.Empty;
            }
        }

        private HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"
            );

            return client;
        }

        private bool IsRedirectStatusCode(HttpStatusCode statusCode)
        {
            int code = (int)statusCode;

            return code == 301 ||
                   code == 302 ||
                   code == 303 ||
                   code == 307 ||
                   code == 308;
        }

        private string BuildAbsoluteRedirectUrl(string originalUrl, Uri location)
        {
            if (location.IsAbsoluteUri)
            {
                return location.ToString();
            }

            var originalUri = new Uri(originalUrl);
            return new Uri(originalUri, location).ToString();
        }
    }
}