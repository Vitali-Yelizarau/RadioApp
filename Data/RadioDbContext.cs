using RadioApp.Models;
using System.Data.Entity;

namespace RadioApp.Data
{
    public class RadioDbContext : DbContext
    {
        public DbSet<MediaItem> MediaItems { get; set; }
        public DbSet<PlayHistoryItem> PlayHistoryItems { get; set; }

        public RadioDbContext()
            : base("name=RadioDbContext")
        {
        }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MediaItem>()
                        .ToTable("MediaItems");

            modelBuilder.Entity<MediaItem>()
                .HasKey(x => x.Id);

            modelBuilder.Entity<MediaItem>()
                .Property(x => x.Title)
                .IsRequired()
                .HasMaxLength(200);

            modelBuilder.Entity<MediaItem>()
                .Property(x => x.Description)
                .HasMaxLength(1000);

            modelBuilder.Entity<MediaItem>()
                .Property(x => x.StreamUrl)
                .IsRequired()
                .HasMaxLength(2000);

            modelBuilder.Entity<MediaItem>()
                .Property(x => x.WebsiteUrl)
                .HasMaxLength(2000);

            modelBuilder.Entity<MediaItem>()
                .Property(x => x.Genre)
                .HasMaxLength(100);

            modelBuilder.Entity<PlayHistoryItem>()
                        .ToTable("PlayHistoryItems");

            modelBuilder.Entity<PlayHistoryItem>()
                .HasKey(x => x.Id);

            modelBuilder.Entity<PlayHistoryItem>()
                .Property(x => x.TrackName)
                .HasMaxLength(500);

            modelBuilder.Entity<PlayHistoryItem>()
                        .HasRequired(x => x.MediaItem)
                        .WithMany(x => x.PlayHistoryItems)
                        .HasForeignKey(x => x.MediaItemId)
                        .WillCascadeOnDelete(true);

            base.OnModelCreating(modelBuilder);
        }
    }
}