using System;

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

            return lower.Contains(".mp3")
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
                || host.Contains(".stream.")
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

            string lower = url.ToLowerInvariant();

            return lower.EndsWith(".png")
                || lower.EndsWith(".jpg")
                || lower.EndsWith(".jpeg")
                || lower.EndsWith(".gif")
                || lower.EndsWith(".svg")
                || lower.EndsWith(".webp")
                || lower.EndsWith(".ico")
                || lower.EndsWith(".css")
                || lower.Contains("/image/")
                || lower.Contains("/images/")
                || lower.Contains("/assets/images/")
                || lower.Contains("recaptcha")
                || lower.Contains("googlesyndication")
                || lower.Contains("accuweather")
                || lower.Contains("33across")
                || lower.Contains("sonobi");
        }

        public bool IsDefinitelyNotStreamUrl(string url)
        {
            string lower = url.ToLowerInvariant();

            return lower.EndsWith(".png")
                || lower.EndsWith(".jpg")
                || lower.EndsWith(".jpeg")
                || lower.EndsWith(".gif")
                || lower.EndsWith(".svg")
                || lower.EndsWith(".webp")
                || lower.EndsWith(".css")
                || lower.EndsWith(".ico")
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