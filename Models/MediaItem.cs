using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RadioApp.Models
{
    public class MediaItem
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; }
        [MaxLength(1000)]
        public string Description { get; set; }
        public MediaSourceType SourceType { get; set; }

        [Required]
        [MaxLength(2000)]
        public string StreamUrl { get; set; }

        [MaxLength(2000)]
        public string WebsiteUrl { get; set; }

        [MaxLength(100)]
        public string Genre { get; set; }

        public int SortOrder { get; set; }
        public int PlayCount { get; set; }

        public bool IsEnabled { get; set; }

        public virtual ICollection<PlayHistoryItem> PlayHistoryItems { get; set; }

        public MediaItem()
        {
            Title = string.Empty;
            Description = string.Empty;
            StreamUrl = string.Empty;
            WebsiteUrl = string.Empty;
            Genre = string.Empty;
            IsEnabled = true;
            PlayHistoryItems = new List<PlayHistoryItem>();
        }
    }
}