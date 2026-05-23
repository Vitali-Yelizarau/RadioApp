using System;
using System.ComponentModel.DataAnnotations;

namespace RadioApp.Models
{
    public class PlayHistoryItem
    {
        public int Id { get; set; }
        public int MediaItemId { get; set; }
        public virtual MediaItem MediaItem { get; set; }
        public DateTime WhenPlayed { get; set; }
        [MaxLength(500)]
        public string TrackName { get; set; }
        public PlayHistoryItem()
        {
            TrackName = string.Empty;
        }
    }
}