using RadioApp.Models;

namespace RadioApp.Services.StreamDiscovery
{
    public class StreamCandidateEvaluation
    {
        public string Url { get; set; }
        public int Score { get; set; }
        public int? Bitrate { get; set; }
        public bool IsHttpConfirmed { get; set; }
        public DiscoveredRadioStream StreamInfo { get; set; }
        public string Reason { get; set; }
    }
}
