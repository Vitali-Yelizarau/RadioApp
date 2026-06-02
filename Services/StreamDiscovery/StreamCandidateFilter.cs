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
            string lower = url.ToLowerInvariant();

            return lower.Contains("player")
                || lower.Contains("play.")
                || lower.Contains("/play")
                || lower.Contains("listen")
                || lower.Contains("online");
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

            if (IsPossibleStreamUrl(url))
            {
                return true;
            }

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
            string path = uri.AbsolutePath.ToLowerInvariant();

            if (lower.Contains("mp3channels.webradio.") ||
                lower.Contains(".webradio.antenne.de"))
            {
                return false;
            }

            if (uri.Host.Equals("www.deutschland.fm", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("deutschland.fm", StringComparison.OrdinalIgnoreCase))
            {
                if (path.Contains("mp3") ||
                    path.Contains("stream") ||
                    path.Contains("icecast") ||
                    path.StartsWith("/radio/"))
                {
                    return true;
                }
            }

            return path.EndsWith(".png")
                || path.EndsWith(".jpg")
                || path.EndsWith(".jpeg")
                || path.EndsWith(".gif")
                || path.EndsWith(".svg")
                || path.EndsWith(".webp")
                || path.EndsWith(".css")
                || path.EndsWith(".ico")
                || lower.Contains("/css/")
                || lower.Contains("/image/")
                || lower.Contains("/images/")
                || lower.Contains("/assets/images/")
                || lower.Contains("recaptcha")
                || lower.Contains("googlesyndication")
                || lower.Contains("googleapis.com/json")
                || lower.Contains("accuweather");
        }
    }
}