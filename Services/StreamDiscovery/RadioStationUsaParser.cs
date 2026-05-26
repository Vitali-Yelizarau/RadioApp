using System;
using System.Text.RegularExpressions;

namespace RadioApp.Services.StreamDiscovery
{
    internal class RadioStationUsaParser
    {
        private readonly StreamUrlExtractor _extractor;

        public RadioStationUsaParser()
        {
            _extractor = new StreamUrlExtractor();
        }

        public string NormalizeCallSign(string callSign)
        {
            if (string.IsNullOrWhiteSpace(callSign))
            {
                return string.Empty;
            }

            return callSign
                .Trim()
                .ToUpperInvariant()
                .Replace("-FM", "")
                .Replace("-AM", "")
                .Replace("-", "");
        }
        public string ExtractRadioStationUsaCallSign(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            string plainText = _extractor.StripHtml(html);

            var match = Regex.Match(
                plainText,
                @"Call\s*sign\s*:?\s*(?<call>[A-Z0-9\-]{3,10})",
                RegexOptions.IgnoreCase
            );

            if (match.Success)
            {
                return NormalizeCallSign(match.Groups["call"].Value);
            }

            match = Regex.Match(
                plainText,
                @"\b[A-Z]{3,5}(?:-FM|-AM)?\b",
                RegexOptions.IgnoreCase
            );

            if (match.Success)
            {
                return NormalizeCallSign(match.Value);
            }

            return string.Empty;
        }
        public bool IsRadioStationUsaPage(string pageUrl)
        {
            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            return uri.Host.IndexOf("radiostationusa.fm", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
