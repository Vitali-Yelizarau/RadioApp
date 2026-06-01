using RadioApp.Models;
using Serilog;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace RadioApp.Services
{
    public class RadioStreamInfoService
    {
        private const int HTTP_TIMEOUT = 15;
        public Task<bool> IsAudioStreamAsync(string streamUrl)
        {
            return IsAudioStreamAsync(streamUrl, HTTP_TIMEOUT);
        }

        public async Task<bool> IsAudioStreamAsync(string streamUrl, int timeoutSeconds)
        {
            try
            {
                using (var client = CreateHttpClient(timeoutSeconds))
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
                    request.Headers.Add("Icy-MetaData", "1");

                    using (var response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            return false;
                        }

                        string contentType = response.Content.Headers.ContentType != null
                            ? response.Content.Headers.ContentType.MediaType
                            : string.Empty;

                        contentType = contentType.ToLowerInvariant();

                        if (contentType.StartsWith("audio/"))
                        {
                            return true;
                        }

                        if (contentType.Contains("mpegurl") ||
                            contentType.Contains("m3u") ||
                            contentType.Contains("aac") ||
                            contentType.Contains("mpeg"))
                        {
                            return true;
                        }

                        if (HasHeader(response, "icy-name") ||
                            HasHeader(response, "icy-metaint") ||
                            HasHeader(response, "icy-br"))
                        {
                            return true;
                        }

                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Stream check failed. StreamUrl: {StreamUrl}", streamUrl);
                return false;
            }
        }

        public Task<DiscoveredRadioStream> ReadStreamInfoAsync(string pageUrl, string streamUrl)
        {
            return ReadStreamInfoAsync(pageUrl, streamUrl, HTTP_TIMEOUT);
        }

        public async Task<DiscoveredRadioStream> ReadStreamInfoAsync(string pageUrl, string streamUrl, int timeoutSeconds)
        {
            using (var client = CreateHttpClient(timeoutSeconds))
            {
                var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
                request.Headers.Add("Icy-MetaData", "1");

                using (var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    string finalStreamUrl = streamUrl;

                    if (response.RequestMessage != null &&
                        response.RequestMessage.RequestUri != null)
                    {
                        finalStreamUrl = response.RequestMessage.RequestUri.ToString();
                    }

                    var result = new DiscoveredRadioStream
                    {
                        PageUrl = pageUrl,
                        StreamUrl = finalStreamUrl,
                        StationName = GetHeader(response, "icy-name"),
                        Description = GetHeader(response, "icy-description"),
                        Genre = GetHeader(response, "icy-genre")
                    };

                    string bitrateText = GetHeader(response, "icy-br");
                    int bitrate;

                    if (int.TryParse(bitrateText, out bitrate))
                    {
                        result.Bitrate = bitrate;
                    }

                    if (string.IsNullOrWhiteSpace(result.StationName))
                    {
                        result.StationName = GetHostName(finalStreamUrl);
                    }

                    return result;
                }
            }
        }

        private HttpClient CreateHttpClient()
        {
            return CreateHttpClient(HTTP_TIMEOUT);
        }

        private HttpClient CreateHttpClient(int timeoutSeconds)
        {
            var client = new HttpClient();

            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 RadioApp/1.0");

            return client;
        }

        private string GetHeader(HttpResponseMessage response, string name)
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                return values.FirstOrDefault() ?? string.Empty;
            }

            if (response.Content.Headers.TryGetValues(name, out values))
            {
                return values.FirstOrDefault() ?? string.Empty;
            }

            return string.Empty;
        }

        private bool HasHeader(HttpResponseMessage response, string name)
        {
            return response.Headers.Contains(name) ||
                   response.Content.Headers.Contains(name);
        }

        private string GetHostName(string url)
        {
            try
            {
                return new Uri(url).Host;
            }
            catch
            {
                return "Unknown radio station";
            }
        }

        public async Task<DiscoveredRadioStream> GetStreamInfoIfPlayableAsync(string pageUrl, string streamUrl)
        {
            try
            {
                bool isAudio = await IsAudioStreamAsync(streamUrl);

                if (!isAudio)
                {
                    return null;
                }

                return await ReadStreamInfoAsync(pageUrl, streamUrl);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to get playable stream info. StreamUrl: {StreamUrl}", streamUrl);
                return null;
            }
        }

        public async Task<DiscoveredRadioStream> GetStreamInfoIfPlayableAsync(
                                                                    string pageUrl,
                                                                    string streamUrl,
                                                                    int timeoutSeconds)
        {
            try
            {
                bool isAudio = await IsAudioStreamAsync(streamUrl, timeoutSeconds);

                if (!isAudio)
                {
                    return null;
                }

                return await ReadStreamInfoAsync(pageUrl, streamUrl, timeoutSeconds);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to get playable stream info. StreamUrl: {StreamUrl}", streamUrl);
                return null;
            }
        }
    }
}