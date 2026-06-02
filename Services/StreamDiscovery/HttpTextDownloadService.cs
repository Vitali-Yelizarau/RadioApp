using Serilog;
using System;
using System.Net.Http;
using System.Text;
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
            return await DownloadTextAsync(url, CancellationToken.None);
        }

        public async Task<string> DownloadTextAsync(
                                    string url,
                                    CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            cancellationToken.ThrowIfCancellationRequested();

            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                linkedCts.CancelAfter(TimeSpan.FromSeconds(HttpTimeoutSeconds));

                using (HttpClient client = CreateHttpClient())
                using (HttpResponseMessage response = await client.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token))
                {
                    response.EnsureSuccessStatusCode();

                    Task<byte[]> readTask = response.Content.ReadAsByteArrayAsync();
                    Task timeoutTask = Task.Delay(
                        TimeSpan.FromSeconds(HttpTimeoutSeconds),
                        linkedCts.Token
                    );

                    Task completedTask = await Task.WhenAny(readTask, timeoutTask);

                    if (completedTask != readTask)
                    {
                        throw new TaskCanceledException(
                            "Text content read timed out. Url: " + url
                        );
                    }

                    byte[] bytes = await readTask;

                    string charset = response.Content.Headers.ContentType != null
                        ? response.Content.Headers.ContentType.CharSet
                        : null;

                    Encoding encoding = GetEncodingSafe(charset);

                    return encoding.GetString(bytes);
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

        private HttpClient CreateHttpClient()
        {
            var client = new HttpClient();

            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"
            );

            client.DefaultRequestHeaders.Accept.ParseAdd(
                "text/html,application/xhtml+xml,application/xml;q=0.9,application/json;q=0.8,text/plain;q=0.8,*/*;q=0.5"
            );

            return client;
        }

        private Encoding GetEncodingSafe(string charset)
        {
            if (string.IsNullOrWhiteSpace(charset))
            {
                return Encoding.UTF8;
            }

            string normalized = charset
                .Trim()
                .Trim('"')
                .Trim('\'')
                .ToLowerInvariant();

            if (normalized == "utf8")
            {
                return Encoding.UTF8;
            }

            if (normalized == "utf-8")
            {
                return Encoding.UTF8;
            }

            try
            {
                return Encoding.GetEncoding(normalized);
            }
            catch
            {
                Log.Debug(
                    "Unsupported charset received from server. Charset: {Charset}. Falling back to UTF-8.",
                    charset
                );

                return Encoding.UTF8;
            }
        }

        public async Task<string> GetFinalUrlAsync(string url)
        {
            return await GetFinalUrlAsync(url, CancellationToken.None);
        }

        public async Task<string> GetFinalUrlAsync(
            string url,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (HttpClient client = CreateHttpClient())
                {
                    using (HttpResponseMessage response = await client.GetAsync(
                        url,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken))
                    {
                        return response.RequestMessage.RequestUri.ToString();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to resolve final URL. Url: {Url}", url);
                return string.Empty;
            }
        }
    }
}