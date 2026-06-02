using RadioApp.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RadioApp.Services
{
    public class HearMeFmDiscoveryService
    {
        private readonly HttpTextDownloadService _httpTextDownloadService;
        private readonly RadioStreamInfoService _streamInfoService;

        public HearMeFmDiscoveryService(
            HttpTextDownloadService httpTextDownloadService,
            RadioStreamInfoService streamInfoService)
        {
            _httpTextDownloadService = httpTextDownloadService;
            _streamInfoService = streamInfoService;
        }

        private static readonly Regex HearMeStreamUrlRegex = new Regex(
            @"https?://radio\.hearme\.fm:\d{2,5}/[a-zA-Z0-9_\-/\.]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        public async Task<DiscoveredRadioStream> TryDiscoverAsync(
            string pageUrl,
            CancellationToken cancellationToken)
        {
            if (!IsHearMePage(pageUrl))
            {
                return null;
            }

            string stationSlug = ExtractSlug(pageUrl);

            if (string.IsNullOrWhiteSpace(stationSlug))
            {
                return null;
            }

            string expectedTitle = SlugToTitle(stationSlug);

            List<string> candidates = await CollectHearMeCandidatesFromRcastAsync(
                expectedTitle,
                stationSlug,
                cancellationToken
            );

            Log.Information(
                            "HearMe.fm RCast candidates collected. ExpectedTitle: {ExpectedTitle}, Count: {Count}",
                            expectedTitle,
                            candidates.Count
                        );

            foreach (string candidate in candidates)
            {
                DiscoveredRadioStream info = await _streamInfoService.GetStreamInfoIfPlayableAsync(
                    pageUrl,
                    candidate
                );

                if (info == null)
                {
                    continue;
                }

                if (LooksLikeRequestedHearMeStation(info, expectedTitle, stationSlug, pageUrl))
                {
                    return info;
                }

                Log.Information(
                    "HearMe.fm candidate rejected. ExpectedTitle: {ExpectedTitle}, CandidateUrl: {CandidateUrl}, CandidateStationName: {CandidateStationName}",
                    expectedTitle,
                    candidate,
                    info.StationName
                );
            }

            return null;
        }

        private static bool IsHearMePage(string pageUrl)
        {
            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            bool isHearMeHost =
                uri.Host.Equals("hearme.fm", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("www.hearme.fm", StringComparison.OrdinalIgnoreCase);

            return isHearMeHost &&
                   uri.AbsolutePath.StartsWith("/radio/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractSlug(string pageUrl)
        {
            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri uri))
            {
                return null;
            }

            string[] parts = uri.AbsolutePath
                .Trim('/')
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
            {
                return null;
            }

            return parts[1];
        }

        private static string SlugToTitle(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return null;
            }

            return string.Join(
                " ",
                slug.Split('-')
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(CapitalizeFirstLetter)
            );
        }

        private static string CapitalizeFirstLetter(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            if (value.Length == 1)
            {
                return value.ToUpperInvariant();
            }

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static string NormalizeHearMeStreamCandidate(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            url = url.Trim();

            if (url.StartsWith("http://radio.hearme.fm:", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url.Substring("http://".Length);
            }

            return url;
        }

        private async Task<List<string>> CollectHearMeCandidatesFromRcastAsync(
                                            string expectedTitle,
                                            string stationSlug,
                                            CancellationToken cancellationToken)
        {
            var result = new List<string>();

            List<string> directoryUrls = BuildRcastDirectoryUrls(expectedTitle, stationSlug);

            foreach (string url in directoryUrls)
            {
                string html = await _httpTextDownloadService.DownloadTextSafeAsync(
                    url,
                    cancellationToken
                );

                if (string.IsNullOrWhiteSpace(html))
                {
                    continue;
                }

                foreach (string rawCandidate in ExtractRadioHearMeStreamUrls(html))
                {
                    string candidate = NormalizeHearMeStreamCandidate(rawCandidate);

                    if (string.IsNullOrWhiteSpace(candidate))
                    {
                        continue;
                    }

                    if (!result.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(candidate);
                    }
                }
            }

            return result;
        }

        private static List<string> BuildRcastDirectoryUrls(string expectedTitle, string stationSlug)
        {
            var result = new List<string>();

            string titleSearch = ToRcastDirectorySearchTerm(expectedTitle);
            string slugSearch = ToRcastDirectorySearchTerm(stationSlug.Replace("-", " "));

            for (int page = 1; page <= 3; page++)
            {
                if (!string.IsNullOrWhiteSpace(titleSearch))
                {
                    result.Add("https://www.rcast.net/dir/" + titleSearch + "/page" + page);
                }

                if (!string.IsNullOrWhiteSpace(slugSearch) &&
                    !slugSearch.Equals(titleSearch, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add("https://www.rcast.net/dir/" + slugSearch + "/page" + page);
                }
            }

            return result;
        }

        private static string ToRcastDirectorySearchTerm(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Trim().ToLowerInvariant();

            normalized = Regex.Replace(normalized, @"\s+", "+");

            return Uri.EscapeDataString(normalized);
        }

        private static IEnumerable<string> ExtractRadioHearMeStreamUrls(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                yield break;
            }

            foreach (Match match in HearMeStreamUrlRegex.Matches(text))
            {
                string url = match.Value;

                if (url.IndexOf("/stream", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    url.IndexOf("/autodj", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    yield return url;
                }
            }
        }

        private static bool LooksLikeRequestedHearMeStation(
                                DiscoveredRadioStream info,
                                string expectedTitle,
                                string stationSlug,
                                string pageUrl)
        {
            if (info == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(info.StreamUrl) ||
                info.StreamUrl.IndexOf("radio.hearme.fm", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(info.StationName))
            {
                string normalizedStationName = NormalizeText(info.StationName);
                string normalizedExpectedTitle = NormalizeText(expectedTitle);
                string normalizedSlug = NormalizeText(stationSlug.Replace("-", " "));

                if (normalizedStationName == normalizedExpectedTitle ||
                    normalizedStationName == normalizedSlug)
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Trim()
                .ToLowerInvariant()
                .Replace("&amp;", "&")
                .Replace("  ", " ");
        }
    }
}
