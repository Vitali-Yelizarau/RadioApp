using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RadioApp.Services.StreamDiscovery
{
    internal class StreamUrlExtractor
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

            return url
                .Trim()
                .TrimEnd(',', ';', ')', ']', '}')
                .Replace("\\/", "/");
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
    }
}
