using RadioApp.Data;
using RadioApp.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Data.SQLite;
using System.Diagnostics;
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
    }
}