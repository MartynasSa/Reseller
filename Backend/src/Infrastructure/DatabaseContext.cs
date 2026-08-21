using Application.Domain.Entities;
using Application.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Infrastructure;

public class DatabaseContext(DbContextOptions<DatabaseContext> options) : DbContext(options)
{
    public DbSet<Client> Clients { get; set; }
    public DbSet<Watch> Watches { get; set; }
    public DbSet<Listing> Listings { get; set; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // All decimal columns default to precision 18, scale 2.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<decimal?>().HavePrecision(18, 2);

        // Normalize all DateTime properties to UTC for Npgsql compatibility.
        // Npgsql requires DateTimeKind.Utc when writing to 'timestamp with time zone'.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    private class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(
                v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }

    private class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcDateTimeConverter()
            : base(
                v => v.HasValue
                    ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime())
                    : v,
                v => v.HasValue
                    ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
                    : v)
        {
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var providerName = Database.ProviderName ?? string.Empty;
        var usePostgresGeneratedGuids = providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);

        var notificationChannelConverter = new ValueConverter<NotificationChannel, string>(
            v => v.ToString().ToLowerInvariant(),
            v => Enum.Parse<NotificationChannel>(v, true));

        var listingSourceConverter = new ValueConverter<ListingSource, string>(
            v => v.ToString().ToLowerInvariant(),
            v => Enum.Parse<ListingSource>(v, true));

        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasKey(e => e.Id);
            var id = entity.Property(e => e.Id).ValueGeneratedOnAdd();
            if (usePostgresGeneratedGuids)
            {
                id.HasDefaultValueSql("gen_random_uuid()");
            }

            entity.HasIndex(e => e.Email).IsUnique();

            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.NotificationChannel).IsRequired()
                .HasConversion(notificationChannelConverter)
                .HasDefaultValue(NotificationChannel.Telegram);
            entity.Property(e => e.NotificationTarget).HasMaxLength(256);
            entity.Property(e => e.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<Watch>(entity =>
        {
            entity.HasKey(e => e.Id);
            var id = entity.Property(e => e.Id).ValueGeneratedOnAdd();
            if (usePostgresGeneratedGuids)
            {
                id.HasDefaultValueSql("gen_random_uuid()");
            }

            entity.HasIndex(e => e.ClientId);
            entity.HasIndex(e => e.IsActive);

            entity.Property(e => e.Keywords).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.Category).HasMaxLength(200);
            entity.Property(e => e.Location).HasMaxLength(200);
            entity.Property(e => e.SourceMarketplaces).IsRequired().HasMaxLength(500);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasOne(e => e.Client)
                .WithMany(c => c.Watches)
                .HasForeignKey(e => e.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Listing>(entity =>
        {
            entity.HasKey(e => e.Id);
            var id = entity.Property(e => e.Id).ValueGeneratedOnAdd();
            if (usePostgresGeneratedGuids)
            {
                id.HasDefaultValueSql("gen_random_uuid()");
            }

            entity.HasIndex(e => e.MatchedWatchId);
            entity.HasIndex(e => e.CreatedAt);

            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Url).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.Source).IsRequired()
                .HasConversion(listingSourceConverter);
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasOne(e => e.MatchedWatch)
                .WithMany(w => w.Listings)
                .HasForeignKey(e => e.MatchedWatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
