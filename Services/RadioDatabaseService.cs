using RadioApp.Data;
using RadioApp.Models;
using Serilog;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace RadioApp.Services
{
    public class RadioDatabaseService
    {
        public async Task InitializeDatabaseAsync()
        {
            Log.Information("Database initialization started.");

            using (var db = new RadioDbContext())
            {
                await CreateTablesIfNotExistAsync(db);
                Log.Debug("Database tables checked/created.");

                if (!db.MediaItems.Any())
                {
                    Log.Information("MediaItems table is empty. Adding default media items.");
                    await AddDefaultMediaItemsAsync(db);
                }
            }

            Log.Information("Database initialization finished.");
        }

        private async Task CreateTablesIfNotExistAsync(RadioDbContext db)
        {
            string createMediaItemsTableSql = @"
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
            ";

            string createPlayHistoryItemsTableSql = @"
                CREATE TABLE IF NOT EXISTS PlayHistoryItems
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    MediaItemId INTEGER NOT NULL,
                    WhenPlayed DATETIME NOT NULL,
                    TrackName TEXT,
                    FOREIGN KEY (MediaItemId) REFERENCES MediaItems(Id) ON DELETE CASCADE
                );
            ";

            await db.Database.ExecuteSqlCommandAsync(createMediaItemsTableSql);
            await db.Database.ExecuteSqlCommandAsync(createPlayHistoryItemsTableSql);
        }

        private async Task AddDefaultMediaItemsAsync(RadioDbContext db)
        {
            db.MediaItems.Add(new MediaItem
            {
                Title = "Radio ROKS",
                Description = "Rock radio from Ukraine",
                SourceType = MediaSourceType.Radio,
                StreamUrl = "https://online.radioroks.ua/RadioROKS_HD",
                WebsiteUrl = "https://www.radioroks.ua/",
                Genre = "Rock",
                SortOrder = 1,
                IsEnabled = true
            });

            db.MediaItems.Add(new MediaItem
            {
                Title = "Radio ROKS Main",
                Description = "Main Radio ROKS stream",
                SourceType = MediaSourceType.Radio,
                StreamUrl = "https://online.radioroks.ua/RadioROKS",
                WebsiteUrl = "https://www.radioroks.ua/",
                Genre = "Rock",
                SortOrder = 2,
                IsEnabled = true
            });

            await db.SaveChangesAsync();
        }

        public async Task<List<MediaItem>> GetEnabledMediaItems()
        {
            using (var db = new RadioDbContext())
            {
                return db.MediaItems
                         .Where(x => x.IsEnabled)
                         .OrderBy(x => x.SortOrder)
                         .ThenBy(x => x.Title)
                         .ToList();
            }
        }

        public List<MediaItem> GetRadioStations()
        {
            using (var db = new RadioDbContext())
            {
                return db.MediaItems
                         .Where(x => x.IsEnabled && x.SourceType == MediaSourceType.Radio)
                         .OrderBy(x => x.SortOrder)
                         .ThenBy(x => x.Title)
                         .ToList();
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
    }
}