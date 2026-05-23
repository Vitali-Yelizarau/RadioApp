using RadioApp.Models;
using System.Data.Entity;

namespace RadioApp.Data
{
    public class RadioDbInitializer : CreateDatabaseIfNotExists<RadioDbContext>
    {
        protected override void Seed(RadioDbContext context)
        {
            context.MediaItems.Add(new MediaItem
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

            context.MediaItems.Add(new MediaItem
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

            context.SaveChanges();

            base.Seed(context);
        }
    }
}