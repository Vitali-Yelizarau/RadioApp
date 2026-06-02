using RadioApp.Data;
using RadioApp.Models;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;

namespace RadioApp.Services
{
    public class RadioDatabaseService
    {
        private void EnsureDatabaseAndTables()
        {
            string connectionString = new SQLiteConnectionStringBuilder
            {
                DataSource = RadioDbContext.DatabasePath,
                ForeignKeys = true
            }.ToString();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                                            CREATE TABLE IF NOT EXISTS MediaItems
                                            (
                                                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                                Title TEXT NOT NULL,
                                                Description TEXT,
                                                SourceType INTEGER NOT NULL,
                                                StreamUrl TEXT NOT NULL,
                                                WebsiteUrl TEXT,
                                                Genre TEXT,
                                                SortOrder INTEGER NOT NULL DEFAULT 0,
                                                IsEnabled INTEGER NOT NULL DEFAULT 1
                                            );

                                            CREATE TABLE IF NOT EXISTS PlayHistoryItems
                                            (
                                                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                                MediaItemId INTEGER NOT NULL,
                                                WhenPlayed DATETIME NOT NULL,
                                                TrackName TEXT,
                                                FOREIGN KEY (MediaItemId) REFERENCES MediaItems(Id) ON DELETE CASCADE
                                            );
                                        ";

                    command.ExecuteNonQuery();
                }
            }
        }

        public async Task<List<MediaItem>> GetEnabledMediaItems()
        {
            using (var db = new RadioDbContext())
            {
                return await db.MediaItems
                                .Where(x => x.IsEnabled)
                                .OrderBy(x => x.SortOrder)
                                .ThenBy(x => x.Title)
                                .ToListAsync();
            }
        }


        public async Task AddPlayHistoryAsync(int mediaItemId, string trackName)
        {
            using (var db = new RadioDbContext())
            {
                db.PlayHistoryItems.Add(new PlayHistoryItem
                {
                    MediaItemId = mediaItemId,
                    TrackName = trackName ?? string.Empty,
                    WhenPlayed = System.DateTime.Now
                });

                await db.SaveChangesAsync();
            }
        }

        public List<PlayHistoryItem> GetRecentHistory(int count)
        {
            using (var db = new RadioDbContext())
            {
                return db.PlayHistoryItems
                         .Include(x => x.MediaItem)
                         .OrderByDescending(x => x.WhenPlayed)
                         .Take(count)
                         .ToList();
            }
        }

        public Task<List<MediaItem>> InitializeDatabaseAndGetEnabledMediaItemsAsync()
        {
            return Task.Run(() =>
            {
                EnsureDatabaseAndTables();

                using (var db = new RadioDbContext())
                {
                    return db.MediaItems
                             .Where(x => x.IsEnabled)
                             .OrderBy(x => x.SortOrder)
                             .ThenBy(x => x.Title)
                             .ToList();
                }
            });
        }

        public void AddMediaItem(MediaItem item)
        {
            using (var db = new RadioDbContext())
            {
                db.MediaItems.Add(item);
                db.SaveChanges();
            }
        }


        public void UpdateRadioStation(MediaItem updatedItem)
        {
            using (var db = new RadioDbContext())
            {
                var item = db.MediaItems.FirstOrDefault(x => x.Id == updatedItem.Id);

                if (item == null)
                {
                    throw new System.InvalidOperationException("Station was not found in database.");
                }

                item.Title = updatedItem.Title ?? string.Empty;
                item.Description = updatedItem.Description ?? string.Empty;
                item.StreamUrl = updatedItem.StreamUrl ?? string.Empty;

                db.SaveChanges();
            }
        }

        private string NormalizeStreamUrl(string streamUrl)
        {
            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                return string.Empty;
            }

            return streamUrl
                .Trim()
                .TrimEnd('/')
                .ToLowerInvariant();
        }

        public MediaItem AddOrUpdateRadioStation(
            string title,
            string description,
            string pageUrl,
            string streamUrl,
            string genre)
        {
            title = Truncate(title, 200);
            description = Truncate(description, 1000);
            pageUrl = Truncate(pageUrl, 1000);
            streamUrl = Truncate(streamUrl, 1000);
            genre = Truncate(genre, 100);

            using (var db = new RadioDbContext())
            {
                string normalizedNewStreamUrl = NormalizeStreamUrl(streamUrl);

                var existingItems = db.MediaItems
                    .Where(x => x.SourceType == MediaSourceType.Radio)
                    .ToList();

                var existingItem = existingItems.FirstOrDefault(x =>
                    NormalizeStreamUrl(x.StreamUrl) == normalizedNewStreamUrl
                );

                if (existingItem != null)
                {
                    existingItem.Title = string.IsNullOrWhiteSpace(title)
                        ? existingItem.Title
                        : title;

                    existingItem.Description = description ?? string.Empty;
                    existingItem.WebsiteUrl = pageUrl ?? string.Empty;
                    existingItem.StreamUrl = streamUrl ?? string.Empty;
                    existingItem.Genre = genre ?? string.Empty;
                    existingItem.IsEnabled = true;

                    db.SaveChanges();

                    return existingItem;
                }

                int nextSortOrder = 1;

                if (db.MediaItems.Any())
                {
                    nextSortOrder = db.MediaItems.Max(x => x.SortOrder) + 1;
                }

                var newItem = new MediaItem
                {
                    Title = title ?? string.Empty,
                    Description = description ?? string.Empty,
                    SourceType = MediaSourceType.Radio,
                    WebsiteUrl = pageUrl ?? string.Empty,
                    StreamUrl = streamUrl ?? string.Empty,
                    Genre = genre ?? string.Empty,
                    SortOrder = nextSortOrder,
                    IsEnabled = true
                };

                db.MediaItems.Add(newItem);
                db.SaveChanges();

                return newItem;
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            value = value.Trim();

            if (value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength);
        }
    }
}