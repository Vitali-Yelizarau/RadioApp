namespace RadioApp.Models
{
    public class DiscoveredRadioStream
    {
        public string PageUrl { get; set; }
        public string StreamUrl { get; set; }
        public string StationName { get; set; }
        public string Description { get; set; }
        public string Genre { get; set; }
        public int? Bitrate { get; set; }
        public string ContentType { get; set; }

        public DiscoveredRadioStream()
        {
            PageUrl = string.Empty;
            StreamUrl = string.Empty;
            StationName = string.Empty;
            Description = string.Empty;
            Genre = string.Empty;
        }
    }
}