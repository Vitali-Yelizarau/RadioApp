using System;
using System.Security.Policy;

namespace RadioApp.Services
{
    public class StreamCandidateFilter
    {
        public bool IsRejectedFinalStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return true;
            }

            string lower = url.ToLowerInvariant();

            return lower.Contains("live.amperwave.net/manifest/")
                || lower.Contains("/manifest/")
                || lower.Contains("fmaac-ibc")
                || lower.Contains("aac-ibc")
                || lower.Contains("aac-hlsc")
                || lower.EndsWith(".m3u")
                || lower.EndsWith(".pls");
        }
        public bool IsRejectedStreamCandidate(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return true;
            }

            string lower = url.ToLowerInvariant();

            if (lower.Contains("cdn-cms.tunein.com/switch/") ||
                lower.Contains("cdn-cms.tunein.com/boost/") ||
                lower.Contains("tunein_switch_intro") ||
                lower.Contains("tunein_switch_outro"))
            {
                return true;
            }

            if (lower.Contains("myradiostream.com/assets/mp3/test.mp3") ||
                lower.Contains("myradiostream.com/embed/assets/mp3/test.mp3") ||
                lower.EndsWith("/assets/mp3/test.mp3"))
            {
                return true;
            }

            return lower.Contains("/programs/file/listen/")
                || lower.Contains("/file/listen/")
                || lower.Contains("/podcast/")
                || lower.Contains("/archive/")
                || lower.Contains("/news/")
                || lower.Contains("/advert")
                || lower.Contains("/ads/")
                || lower.Contains("/banner/")
                || lower.Contains("/episode/")
                || lower.Contains("/episodes/")
                || lower.Contains("/record/")
                || lower.Contains("/records/");
        }
        public bool IsPossibleJsonOrApiUrl(string url)
        {
            string lower = url.ToLowerInvariant();

            if (IsDefinitelyNotTextUrl(lower))
            {
                return false;
            }

            return lower.Contains("/api/")
                || lower.Contains("/api?")
                || lower.EndsWith(".json")
                || lower.Contains(".json?")
                || lower.Contains("/json/")
                || lower.EndsWith("/json")
                || lower.Contains("/config/")
                || lower.Contains("/streams/")
                || lower.Contains("/playlist/");
        }
        public bool IsPossiblePlayerPageUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string lower = url.ToLowerInvariant();
            string path = uri.AbsolutePath.ToLowerInvariant();

            if (IsDefinitelyNotTextUrl(url))
            {
                return false;
            }

            /*
             * API endpoints are not player pages.
             * They can be checked by JSON/API logic, but we should not recursively parse them
             * as HTML pages, otherwise we keep downloading the same JS/API links again and again.
             */
            if (path.Contains("/api/") ||
                path.StartsWith("/api") ||
                lower.Contains(".json") ||
                lower.Contains(".m3u") ||
                lower.Contains(".pls") ||
                lower.EndsWith(".js") ||
                lower.Contains(".js?") ||
                lower.EndsWith(".css") ||
                lower.Contains(".css?"))
            {
                return false;
            }

            return lower.Contains("player")
                || lower.Contains("play.")
                || path.Contains("/play")
                || path.Contains("/listen")
                || path.Contains("/online");
        }

        public bool IsPossibleStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string lower = url.ToLowerInvariant();
            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath.ToLowerInvariant();

            bool isMyRadioStreamShoutcastEndpoint =
                    host.EndsWith(".myradiostream.com") &&
                    (
                        !uri.IsDefaultPort ||
                        path.Contains("/;") ||
                        System.Text.RegularExpressions.Regex.IsMatch(path, @"^/\d+/?;")
                    );

            return lower.Contains("mp3channels.webradio.")
                || lower.Contains(".webradio.antenne.de")

                || lower.Contains(".mp3")
                || lower.Contains(".aac")
                || lower.Contains(".ogg")
                || lower.Contains(".opus")
                || lower.Contains(".m3u8")
                || lower.Contains(".pls")
                || lower.Contains(".m3u")

                || path.Contains("/mp3-")
                || path.Contains("/aac-")
                || path.Contains("mp3-")
                || path.Contains("aac-")
                || path.Contains("opus-")
                || path.Contains("ogg-")

                || path.Contains("/stream")
                || path.Contains("/streams")
                || path.Contains("/live")
                || path.Contains("/listen")
                || path.Contains("/radio")
                || path.Contains("/manifest")

                || host.StartsWith("online.")
                || host.StartsWith("stream.")
                || host.StartsWith("streams.")
                || host.Contains(".stream.")
                || host.Contains(".streams.")
                || host.Contains("stream.")
                || isMyRadioStreamShoutcastEndpoint
                || host.Contains(".stream.vip")
                || host.EndsWith("stream.vip")

                || lower.Contains("tritondigital")
                || lower.Contains("streamguys")
                || lower.Contains("icecast")
                || lower.Contains("shoutcast")
                || lower.Contains("securenetsystems.net/v5/")
                || lower.Contains("envisionwise")
                || lower.Contains("playerservices")
                || lower.Contains("amperwave");
        }
        public bool IsDefinitelyNotTextUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return true;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return true;
            }

            string lower = url.ToLowerInvariant();
            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath.ToLowerInvariant();

            /*
             * MyRadioStream JSON endpoint is a real text/API resource.
             * It can contain the real Shoutcast host/port for the embedded player.
             *
             * Examples:
             * https://myradiostream.com/json.php?s=MetalExpressRadio&nocache=...
             * https://myradiostream.com/embed/json.php?s=MetalExpressRadio&nocache=...
             */
            if (host.Equals("myradiostream.com", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith("/json.php"))
            {
                return false;
            }

            /*
             * MyRadioStream embed pages are HTML/PHP player pages.
             * They may contain the real Shoutcast stream URL.
             *
             * Example:
             * https://myradiostream.com/embed/basic.php?s=MetalExpressRadio&btnstyle=circle
             */
            if (host.Equals("myradiostream.com", StringComparison.OrdinalIgnoreCase) &&
                path.Contains("/embed/") &&
                path.EndsWith(".php"))
            {
                return false;
            }

            /*
             * M3U / PLS are playlist files.
             * They are text and must be downloaded so we can extract real stream URLs from them.
             *
             * Keep this block BEFORE IsPossibleStreamUrl(url), because some playlist/API URLs
             * may also look stream-like.
             */
            if (path.EndsWith(".m3u") ||
                path.EndsWith(".m3u8") ||
                path.EndsWith(".pls") ||
                lower.Contains(".m3u?") ||
                lower.Contains(".m3u8?") ||
                lower.Contains(".pls?") ||
                lower.Contains("/api/m3u/"))
            {
                return false;
            }

            /*
             * Static assets are not useful text sources for stream discovery.
             */
            if (lower.EndsWith(".png") ||
                lower.EndsWith(".jpg") ||
                lower.EndsWith(".jpeg") ||
                lower.EndsWith(".gif") ||
                lower.EndsWith(".svg") ||
                lower.EndsWith(".webp") ||
                lower.EndsWith(".css") ||
                lower.EndsWith(".ico") ||
                lower.EndsWith(".woff") ||
                lower.EndsWith(".woff2") ||
                lower.EndsWith(".ttf"))
            {
                return true;
            }

            if (lower.Contains("/image/") ||
                lower.Contains("/images/") ||
                lower.Contains("/static/image/") ||
                lower.Contains("/assets/images/"))
            {
                return true;
            }

            /*
             * Real live streams should not be downloaded as text.
             * They should be checked by RadioStreamInfoService via headers.
             */
            if (IsPossibleStreamUrl(url))
            {
                return true;
            }

            /*
             * Known non-useful external resources.
             */
            if (host.Contains("googlesyndication") ||
                host.Contains("doubleclick") ||
                host.Contains("google-analytics") ||
                host.Contains("facebook.com") ||
                host.Contains("twitter.com") ||
                host.Contains("x.com"))
            {
                return true;
            }

            return false;
        }

        public bool IsDefinitelyNotStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return true;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return true;
            }

            string lower = url.ToLowerInvariant();
            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath.ToLowerInvariant();


            if (lower.Contains("myradiostream.com/assets/mp3/test.mp3") ||
                lower.Contains("myradiostream.com/embed/assets/mp3/test.mp3") ||
                lower.EndsWith("/assets/mp3/test.mp3"))
            {
                return true;
            }


            /*
             * radio.net / radio.dk / radio.at station pages are catalog pages,
             * not playable stream URLs.
             */
            if ((host.Equals("www.radio.net", StringComparison.OrdinalIgnoreCase) ||
                 host.Equals("radio.net", StringComparison.OrdinalIgnoreCase) ||
                 host.Equals("www.radio.dk", StringComparison.OrdinalIgnoreCase) ||
                 host.Equals("radio.dk", StringComparison.OrdinalIgnoreCase) ||
                 host.Equals("www.radio.at", StringComparison.OrdinalIgnoreCase) ||
                 host.Equals("radio.at", StringComparison.OrdinalIgnoreCase) ||
                 host.Equals("www.radio.de", StringComparison.OrdinalIgnoreCase) ||
                 host.Equals("radio.de", StringComparison.OrdinalIgnoreCase)) &&
                path.StartsWith("/s/"))
            {
                return true;
            }

            /*
             * TuneIn radio pages are pages, not streams.
             */
            if ((host.Equals("tunein.com", StringComparison.OrdinalIgnoreCase) ||
                 host.Equals("www.tunein.com", StringComparison.OrdinalIgnoreCase)) &&
                path.StartsWith("/radio/"))
            {
                return true;
            }

            /*
             * TuneIn service sounds. These are valid MP3 files, but not radio streams.
             */
            if (lower.Contains("cdn-cms.tunein.com/switch/") ||
                lower.Contains("cdn-cms.tunein.com/boost/") ||
                lower.Contains("tunein_switch_intro") ||
                lower.Contains("tunein_switch_outro"))
            {
                return true;
            }

            /*
             * Static assets.
             */
            if (lower.EndsWith(".png") ||
                lower.EndsWith(".jpg") ||
                lower.EndsWith(".jpeg") ||
                lower.EndsWith(".gif") ||
                lower.EndsWith(".svg") ||
                lower.EndsWith(".webp") ||
                lower.EndsWith(".css") ||
                lower.EndsWith(".ico") ||
                lower.EndsWith(".woff") ||
                lower.EndsWith(".woff2") ||
                lower.EndsWith(".ttf"))
            {
                return true;
            }

            if (lower.Contains("/image/") ||
                lower.Contains("/images/") ||
                lower.Contains("/static/image/") ||
                lower.Contains("/assets/images/") ||
                lower.Contains("station-images-prod.radio-assets.com"))
            {
                return true;
            }

            /*
             * Web pages / app pages that often contain "radio", "stream", "live",
             * but are not stream URLs.
             */
            if (host.Contains("facebook.com") ||
                host.Contains("instagram.com") ||
                host.Contains("twitter.com") ||
                host.Contains("x.com") ||
                host.Contains("linkedin.com") ||
                host.Contains("youtube.com") ||
                host.Contains("google.com") ||
                host.Contains("googletagmanager.com") ||
                host.Contains("googleadservices.com") ||
                host.Contains("doubleclick.net") ||
                host.Contains("cookielaw.org") ||
                host.Contains("usercentrics.eu"))
            {
                return true;
            }

            return false;
        }
    }
}