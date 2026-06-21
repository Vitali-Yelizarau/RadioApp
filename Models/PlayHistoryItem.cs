using System;
using System.ComponentModel.DataAnnotations;

namespace RadioApp.Models
{
    public enum PlaybackEventType
    {
        Started,
        Stopped,
        Error,
        TrackChanged
    }

    public class PlayHistoryItem
    {
        public int Id { get; set; }
        public int MediaItemId { get; set; }
        public virtual MediaItem MediaItem { get; set; }

        public DateTime EventTime { get; set; }

        /// <summary>
        /// Stored as plain text (e.g. "Started", "Stopped", "Error", "TrackChanged")
        /// so the play history table is self-documenting when viewed directly in a
        /// SQLite browser. Set this from a PlaybackEventType value's ToString().
        /// </summary>
        [MaxLength(50)]
        public string EventType { get; set; }

        /// <summary>
        /// Free-form note attached to the event. For TrackChanged events this holds
        /// the track title read from ICY metadata. Other event types may leave this
        /// empty, or use it for future details (e.g. an error message).
        /// </summary>
        [MaxLength(500)]
        public string Comment { get; set; }

        public PlayHistoryItem()
        {
            EventType = string.Empty;
            Comment = string.Empty;
        }
    }
}