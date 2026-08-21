using Application.Domain.Entities;
using Application.Domain.Enums;
using Application.Models.Polling;
using Application.Ports;
using Application.Services;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Application.Tests;

/// <summary>
/// Exercises <see cref="WatchPollingWorker.PollOnceAsync"/> (a single poll cycle) against an EF
/// Core InMemory-backed <see cref="DatabaseContext"/> and fake <see cref="IListingSource"/>/
/// <see cref="INotificationSender"/> implementations. No live database or real marketplace/
/// Telegram credentials are used or required.
///
/// Focus is the two documented correctness properties that are easy to get wrong in this
/// pattern: (1) a single Watch whose source throws does not stop other Watches in the same
/// cycle from being processed and persisted, and (2) a single Listing's notification failing
/// does not stop notifications for the other new Listings.
/// </summary>
public class WatchPollingWorkerTests
{
    private static ServiceProvider BuildProvider(string dbName, IEnumerable<IListingSource> sources, INotificationSender notificationSender)
    {
        var services = new ServiceCollection();
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped<IListingFilterService, ListingFilterService>();
        foreach (var source in sources)
        {
            services.AddSingleton(typeof(IListingSource), source);
        }
        services.AddSingleton(notificationSender);
        services.Configure<PollingOptions>(o => o.IntervalMinutes = 5);
        return services.BuildServiceProvider();
    }

    private static Client MakeClient() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Client",
        Email = $"{Guid.NewGuid()}@example.com",
        NotificationChannel = NotificationChannel.Telegram,
        NotificationTarget = "12345",
        CreatedAt = DateTime.UtcNow,
    };

    private static Watch MakeWatch(Client client) => new()
    {
        Id = Guid.NewGuid(),
        ClientId = client.Id,
        Client = client,
        Keywords = "iphone",
        Location = "newyork",
        SourceMarketplaces = "eBay",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
    };

    private static Listing MakeListing(Watch watch, string title, ListingSource source = ListingSource.eBay) => new()
    {
        Title = title,
        Price = 100m,
        Url = $"https://example.com/{Guid.NewGuid()}",
        Source = source,
        MatchedWatchId = watch.Id,
        CreatedAt = DateTime.UtcNow,
    };

    private sealed class FakeListingSource : IListingSource
    {
        private readonly Func<Watch, IReadOnlyList<Listing>> _resultFactory;
        private readonly Exception? _throwFor;
        private readonly Func<Watch, bool> _shouldThrow;

        public FakeListingSource(Func<Watch, IReadOnlyList<Listing>> resultFactory, Func<Watch, bool>? shouldThrow = null, Exception? throwFor = null)
        {
            _resultFactory = resultFactory;
            _shouldThrow = shouldThrow ?? (_ => false);
            _throwFor = throwFor ?? new InvalidOperationException("simulated source failure");
        }

        public List<Watch> CallsReceived { get; } = new();

        public Task<IReadOnlyList<Listing>> SearchAsync(Watch watch, CancellationToken cancellationToken = default)
        {
            CallsReceived.Add(watch);
            if (_shouldThrow(watch))
            {
                throw _throwFor!;
            }

            return Task.FromResult(_resultFactory(watch));
        }
    }

    private sealed class FakeNotificationSender : INotificationSender
    {
        private readonly Func<Listing, bool> _shouldThrow;

        public FakeNotificationSender(Func<Listing, bool>? shouldThrow = null)
        {
            _shouldThrow = shouldThrow ?? (_ => false);
        }

        public List<Listing> Notified { get; } = new();

        public Task SendListingNotificationAsync(Client client, Listing listing, CancellationToken cancellationToken = default)
        {
            if (_shouldThrow(listing))
            {
                throw new InvalidOperationException("simulated notification failure");
            }

            Notified.Add(listing);
            return Task.CompletedTask;
        }
    }

    private static WatchPollingWorker CreateWorker(ServiceProvider provider)
    {
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var options = provider.GetRequiredService<IOptions<PollingOptions>>();
        return new WatchPollingWorker(scopeFactory, options, NullLogger<WatchPollingWorker>.Instance);
    }

    /// <summary>
    /// Resolves a scoped <see cref="DatabaseContext"/> via a fresh, disposable
    /// <see cref="IServiceScope"/> - resolving scoped services directly off the root
    /// <see cref="ServiceProvider"/> and disposing them individually conflicts with the root
    /// provider's own disposal, so tests must create their own scope per seed/verify step, the
    /// same way <see cref="WatchPollingWorker.PollOnceAsync"/> does per poll tick.
    /// </summary>
    private static async Task<IServiceScope> SeedAsync(ServiceProvider provider, params object[] entities)
    {
        var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        context.AddRange(entities);
        await context.SaveChangesAsync();
        return scope;
    }

    [Fact]
    public async Task PollOnceAsync_PersistsNewListings_AndNotifiesClient_ForActiveWatch()
    {
        var dbName = Guid.NewGuid().ToString();
        var client = MakeClient();
        var watch = MakeWatch(client);

        var source = new FakeListingSource(w => new[] { MakeListing(w, "iPhone 12 64GB") });
        var notificationSender = new FakeNotificationSender();

        using var provider = BuildProvider(dbName, new[] { source }, notificationSender);
        using (await SeedAsync(provider, client, watch)) { }

        var worker = CreateWorker(provider);
        await worker.PollOnceAsync(CancellationToken.None);

        using var verifyScope = provider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DatabaseContext>();
        Assert.Single(verifyContext.Listings.Where(l => l.MatchedWatchId == watch.Id));
        Assert.Single(notificationSender.Notified);
    }

    [Fact]
    public async Task PollOnceAsync_SkipsInactiveWatches()
    {
        var dbName = Guid.NewGuid().ToString();
        var client = MakeClient();
        var watch = MakeWatch(client);
        watch.IsActive = false;

        var source = new FakeListingSource(w => new[] { MakeListing(w, "iPhone 12 64GB") });
        var notificationSender = new FakeNotificationSender();

        using var provider = BuildProvider(dbName, new[] { source }, notificationSender);
        using (await SeedAsync(provider, client, watch)) { }

        var worker = CreateWorker(provider);
        await worker.PollOnceAsync(CancellationToken.None);

        Assert.Empty(source.CallsReceived);
        Assert.Empty(notificationSender.Notified);
    }

    [Fact]
    public async Task PollOnceAsync_OneWatchSourceFailure_DoesNotPreventOtherWatchesFromBeingProcessed()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = MakeClient();
        var watchA = MakeWatch(clientA); // will fail
        var clientB = MakeClient();
        var watchB = MakeWatch(clientB); // should still succeed

        var source = new FakeListingSource(
            w => new[] { MakeListing(w, "Good listing") },
            shouldThrow: w => w.Id == watchA.Id);
        var notificationSender = new FakeNotificationSender();

        using var provider = BuildProvider(dbName, new[] { source }, notificationSender);
        using (await SeedAsync(provider, clientA, watchA, clientB, watchB)) { }

        var worker = CreateWorker(provider);
        await worker.PollOnceAsync(CancellationToken.None);

        using var verifyScope = provider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DatabaseContext>();
        Assert.Empty(verifyContext.Listings.Where(l => l.MatchedWatchId == watchA.Id));
        Assert.Single(verifyContext.Listings.Where(l => l.MatchedWatchId == watchB.Id));
        Assert.Single(notificationSender.Notified);
    }

    [Fact]
    public async Task PollOnceAsync_OneNotificationFailure_DoesNotPreventOtherNotifications()
    {
        var dbName = Guid.NewGuid().ToString();
        var client = MakeClient();
        var watch = MakeWatch(client);

        var listingToFail = MakeListing(watch, "Bad listing (notify fails)");
        var listingToSucceed = MakeListing(watch, "Good listing (notify succeeds)");

        var source = new FakeListingSource(w => new[] { listingToFail, listingToSucceed });
        var notificationSender = new FakeNotificationSender(shouldThrow: l => l.Title == listingToFail.Title);

        using var provider = BuildProvider(dbName, new[] { source }, notificationSender);
        using (await SeedAsync(provider, client, watch)) { }

        var worker = CreateWorker(provider);
        await worker.PollOnceAsync(CancellationToken.None);

        // Both listings should still be persisted even though one notification failed...
        using var verifyScope = provider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DatabaseContext>();
        Assert.Equal(2, verifyContext.Listings.Count(l => l.MatchedWatchId == watch.Id));

        // ...but only the successful one should have actually been notified.
        Assert.Single(notificationSender.Notified);
        Assert.Equal(listingToSucceed.Title, notificationSender.Notified[0].Title);
    }

    [Fact]
    public async Task PollOnceAsync_DoesNotReNotify_ListingsAlreadyStoredForWatch()
    {
        var dbName = Guid.NewGuid().ToString();
        var client = MakeClient();
        var watch = MakeWatch(client);

        var existingListing = MakeListing(watch, "iPhone 12 64GB");
        var source = new FakeListingSource(w => new[] { MakeListing(w, "iPhone 12 64GB") });
        var notificationSender = new FakeNotificationSender();

        using var provider = BuildProvider(dbName, new[] { source }, notificationSender);
        using (await SeedAsync(provider, client, watch, existingListing)) { }

        var worker = CreateWorker(provider);
        await worker.PollOnceAsync(CancellationToken.None);

        using var verifyScope = provider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DatabaseContext>();
        // Still just the one pre-existing listing - the "new" candidate was a dedup match.
        Assert.Single(verifyContext.Listings.Where(l => l.MatchedWatchId == watch.Id));
        Assert.Empty(notificationSender.Notified);
    }

    [Fact]
    public void PollingOptions_DefaultsToFiveMinutes()
    {
        var options = new PollingOptions();

        Assert.Equal(5, options.IntervalMinutes);
    }
}
