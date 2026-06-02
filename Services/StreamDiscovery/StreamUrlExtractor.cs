using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace RadioApp.Services.StreamDiscovery
{
    public class StreamUrlExtractor
    {
        private readonly StreamCandidateFilter _filter;

        public StreamUrlExtractor()
        {
            _filter = new StreamCandidateFilter();
        }
        public string StripHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            string withoutScripts = Regex.Replace(
                html,
                @"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>",
                " ",
                RegexOptions.IgnoreCase
            );

            string withoutStyles = Regex.Replace(
                withoutScripts,
                @"<style\b[^<]*(?:(?!<\/style>)<[^<]*)*<\/style>",
                " ",
                RegexOptions.IgnoreCase
            );

            string plainText = Regex.Replace(
                withoutStyles,
                "<.*?>",
                " ",
                RegexOptions.IgnoreCase
            );

            return Regex.Replace(plainText, @"\s+", " ").Trim();
        }
        public string DecodeJavaScriptString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\/", "/")
                .Replace("\\u002F", "/")
                .Replace("\\u002f", "/")
                .Replace("&amp;", "&");
        }
        public string CleanUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            url = WebUtility.HtmlDecode(url);
            url = url.Replace("&amp;", "&");

            url = url.Trim()
                     .TrimEnd('.', ',', ';', ')', ']', '}', '"', '\'');

            return url;
        }
        public string NormalizeUrl(string url, string baseUrl)
        {
            url = CleanUrl(url);

            if (url.StartsWith("//"))
            {
                var baseUri = new Uri(baseUrl);
                return baseUri.Scheme + ":" + url;
            }

            if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                return url;
            }

            var baseUriForRelative = new Uri(baseUrl);
            return new Uri(baseUriForRelative, url).ToString();
        }
        public List<string> ExtractIframeUrls(string html, string baseUrl)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(html))
                return result;

            var matches = Regex.Matches(
                html,
                @"<iframe[^>]+src=[""'](?<src>[^""']+)[""']",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in matches)
            {
                string src = match.Groups["src"].Value;

                try
                {
                    result.Add(NormalizeUrl(src, baseUrl));
                }
                catch
                {
                    // Ignore invalid iframe URL.
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        public List<string> ExtractJavaScriptUrls(string html, string baseUrl)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(html))
            {
                return result;
            }

            var matches = Regex.Matches(
                html,
                @"<script[^>]+src=[""'](?<src>[^""']+)[""']",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in matches)
            {
                string src = match.Groups["src"].Value;

                try
                {
                    string url = NormalizeUrl(src, baseUrl);

                    if (url.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                        url.IndexOf(".js?", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Add(url);
                    }
                }
                catch
                {
                    // Ignore invalid script URL.
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<string> ExtractPossiblePlayerPages(string html, string baseUrl)
        {
            return ExtractAllUrls(html, baseUrl)
                .Where(_filter.IsPossiblePlayerPageUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        public List<string> ExtractAllUrls(string text, string baseUrl)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            var matches = Regex.Matches(
                text,
                @"(?:(?:https?:)?//|/)[^'""<>\s\\]+",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in matches)
            {
                string rawUrl = match.Value.Trim();

                try
                {
                    string normalizedUrl = NormalizeUrl(rawUrl, baseUrl);
                    result.Add(normalizedUrl);
                }
                catch
                {
                    // Ignore invalid URL candidates.
                }
            }

            return result;
        }
        public List<string> ExtractStreamUrlsFromStructuredText(string text, string baseUrl)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            var patterns = new[]
            {
                @"(?:stream|streamUrl|stream_url|streamURL|audio|audioUrl|audio_url|src|url|source)\s*[:=]\s*[""'](?<url>[^""']+)[""']",
                @"data-(?:stream|url|src|audio|audio-url|stream-url)=[""'](?<url>[^""']+)[""']",
                @"""(?:stream|streamUrl|stream_url|audio|audioUrl|audio_url|src|url|source)""\s*:\s*""(?<url>[^""]+)"""
            };

            foreach (string pattern in patterns)
            {
                var matches = Regex.Matches(
                    text,
                    pattern,
                    RegexOptions.IgnoreCase
                );

                foreach (Match match in matches)
                {
                    string rawUrl = match.Groups["url"].Value;

                    if (string.IsNullOrWhiteSpace(rawUrl))
                    {
                        continue;
                    }

                    rawUrl = DecodeJavaScriptString(rawUrl);

                    try
                    {
                        string normalizedUrl = NormalizeUrl(rawUrl, baseUrl);

                        if (_filter.IsPossibleStreamUrl(normalizedUrl) &&
                            !_filter.IsRejectedStreamCandidate(normalizedUrl) &&
                            !_filter.IsDefinitelyNotStreamUrl(normalizedUrl))
                        {
                            result.Add(normalizedUrl);
                        }
                    }
                    catch
                    {
                        // Ignore invalid URL.
                    }
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        public List<string> ExtractStreamLikeUrls(string text, string baseUrl)
        {
            var allUrls = ExtractAllUrls(text, baseUrl);

            return allUrls
                .Where(_filter.IsPossibleStreamUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<string> ExtractPossibleJsonApiUrls(string text, string baseUrl)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            List<string> allUrls = ExtractAllUrls(text, baseUrl);

            foreach (string url in allUrls)
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                {
                    continue;
                }

                string lower = url.ToLowerInvariant();
                string host = uri.Host.ToLowerInvariant();
                string path = uri.AbsolutePath.ToLowerInvariant();

                /*
                 * IMPORTANT:
                 * Playlist / stream endpoints are NOT JSON/API text resources.
                 * They are handled by playlist extraction or by stream validation.
                 *
                 * If we allow them here, the JSON/API extractor may try to download
                 * an endless audio stream as text and hang until timeout.
                 */
                bool isPlaylistUrl =
                    path.EndsWith(".m3u") ||
                    path.EndsWith(".m3u8") ||
                    path.EndsWith(".pls") ||
                    lower.Contains(".m3u?") ||
                    lower.Contains(".m3u8?") ||
                    lower.Contains(".pls?") ||
                    path.Contains("/m3u/") ||
                    path.Contains("/pls/") ||
                    path.Contains("/playlist.m3u") ||
                    path.Contains("/playlist.pls");

                if (isPlaylistUrl)
                {
                    continue;
                }

                /*
                 * These are likely playable stream endpoints, not JSON/API endpoints.
                 * Do not download them as text.
                 */
                bool isStreamLikeEndpoint =
                    path.Contains("/stream") ||
                    path.Contains("/live") ||
                    path.Contains("/listen") ||
                    path.Contains("/aac") ||
                    path.Contains("/mp3") ||
                    path.Contains("/ogg") ||
                    path.Contains("/opus") ||
                    host.StartsWith("stream.") ||
                    host.StartsWith("streams.") ||
                    host.Contains(".stream.") ||
                    host.Contains(".streams.") ||
                    host.Contains("icecast") ||
                    host.Contains("shoutcast");

                if (isStreamLikeEndpoint)
                {
                    continue;
                }

                bool isStaticAsset =
                    lower.EndsWith(".png") ||
                    lower.EndsWith(".jpg") ||
                    lower.EndsWith(".jpeg") ||
                    lower.EndsWith(".gif") ||
                    lower.EndsWith(".svg") ||
                    lower.EndsWith(".webp") ||
                    lower.EndsWith(".css") ||
                    lower.EndsWith(".ico") ||
                    lower.EndsWith(".woff") ||
                    lower.EndsWith(".woff2") ||
                    lower.EndsWith(".ttf") ||
                    lower.Contains("/image/") ||
                    lower.Contains("/images/") ||
                    lower.Contains("/static/image/") ||
                    lower.Contains("/assets/images/");

                if (isStaticAsset)
                {
                    continue;
                }

                bool isKnownBad =
                    lower.Contains("developer.mozilla.org") ||
                    lower.Contains("code.jquery.com/json") ||
                    lower.Contains("vjs.zencdn.net/json") ||
                    lower.Contains("googlesyndication") ||
                    lower.Contains("googleapis.com") ||
                    lower.Contains("googleadservices.com") ||
                    lower.Contains("googletagmanager.com") ||
                    lower.Contains("doubleclick") ||
                    lower.Contains("facebook.com") ||
                    lower.Contains("twitter.com") ||
                    lower.Contains("x.com/") ||
                    lower.Contains("usercentrics") ||
                    lower.Contains("cookielaw");

                if (isKnownBad)
                {
                    continue;
                }

                /*
                 * Keep this strict.
                 * Do not add generic words like "stream", "radio", "listen", "player" here.
                 * Those made the extractor too greedy before.
                 */
                bool looksLikeJsonOrApi =
                        lower.EndsWith(".json") ||
                        lower.Contains(".json?") ||
                        path.EndsWith("/json") ||
                        path.EndsWith("/json.php") ||
                        path.Contains("/json.php") ||
                        path.Contains("/api/") ||
                        host.StartsWith("api.") ||
                        host.Contains(".api.") ||
                        path.Contains("/ajax/") ||
                        path.Contains("ajax.php") ||
                        lower.Contains("stations.json") ||
                        lower.Contains("channels.json");

                if (looksLikeJsonOrApi)
                {
                    result.Add(url);
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
