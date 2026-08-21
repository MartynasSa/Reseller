using Application.Domain.Entities;
using Application.Models.Polling;
using Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>
/// Hosted background service that periodically runs every active <see cref="Watch"/> against
/// all registered <see cref="IListingSource"/>s, filters/dedups the results, persists newly
/// accepted <see cref="Listing"/>s, and notifies the owning <see cref="Client"/> for each one.
///
/// Lifecycle/DI note: this class is registered as a singleton (via <c>AddHostedService</c>), but
/// <see cref="DatabaseContext"/> and the scoped application services (<see cref="IListingSource"/>,
/// <see cref="IListingFilterService"/>, <see cref="INotificationSender"/>) are all registered
/// Scoped (see ServiceCollectionExtensions). EF Core's DbContext is explicitly not thread-safe
/// and not meant to be held for a long-running singleton's lifetime, so a new
/// <see cref="IServiceScope"/> is created per poll tick (not per Watch - one scope/DbContext is
/// reused across all Watches within a single tick, which is the standard EF Core pattern for
/// "one unit of work per background iteration") and disposed at the end of the tick.
///
/// Error isolation: a Watch whose source query, filtering, or persistence throws is logged and
/// skipped - it must not prevent other Watches in the same tick from being processed. Similarly,
/// a single Listing's notification failing must not stop notifications for other new Listings
/// (for the same Watch or others). Both isolation boundaries are implemented as nested
/// try/catch inside the per-Watch and per-Listing loops respectively, rather than wrapping the
/// whole tick, so a Watch/Listing that already succeeded is never rolled back or skipped because
/// of a later failure.
/// </summary>
public class WatchPollingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PollingOptions _options;
    private readonly ILogger<WatchPollingWorker> _logger;

    public WatchPollingWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<PollingOptions> options,
        ILogger<WatchPollingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_options.IntervalMinutes <= 0 ? 5 : _options.IntervalMinutes);
        _logger.LogInformation("WatchPollingWorker started with interval {IntervalMinutes} minutes", interval.TotalMinutes);

        using var timer = new PeriodicTimer(interval);

        // Run an immediate poll on startup, then wait for the timer before each subsequent poll,
        // so newly-created Watches don't wait a full interval before their first run.
        do
        {
            try
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down mid-cycle - fall through to the outer loop condition, which will
                // exit because WaitForNextTickAsync below observes the same token.
            }
            catch (Exception ex)
            {
                // Defense in depth: PollOnceAsync already isolates per-Watch/per-Listing
                // failures, so this should only fire on something catastrophic (e.g. failure to
                // resolve a DI scope). Log and keep the timer loop alive rather than crashing the
                // whole hosted service.
                _logger.LogError(ex, "Unhandled error in WatchPollingWorker poll cycle");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));

        _logger.LogInformation("WatchPollingWorker stopped");
    }

    /// <summary>
    /// Runs a single poll cycle (one DI scope, all active Watches) and returns. Public - separate
    /// from <see cref="ExecuteAsync"/>'s timer loop - specifically so tests can exercise the
    /// per-Watch/per-Listing error isolation and DI-scoping behavior directly without needing to
    /// drive the full BackgroundService/PeriodicTimer lifecycle.
    /// </summary>
    public async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var listingSources = scope.ServiceProvider.GetRequiredService<IEnumerable<IListingSource>>();
        var filterService = scope.ServiceProvider.GetRequiredService<IListingFilterService>();
        var notificationSender = scope.ServiceProvider.GetRequiredService<INotificationSender>();

        var activeWatches = await dbContext.Watches
            .Include(w => w.Client)
            .Where(w => w.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Polling {WatchCount} active watches", activeWatches.Count);

        foreach (var watch in activeWatches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ProcessWatchAsync(watch, dbContext, listingSources, filterService, notificationSender, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One Watch's failure (a source outage, a bad DB write, etc.) must not stop the
                // rest of the batch from being polled.
                _logger.LogError(ex, "Error processing watch {WatchId}", watch.Id);
            }
        }
    }

    private async Task ProcessWatchAsync(
        Watch watch,
        DatabaseContext dbContext,
        IEnumerable<IListingSource> listingSources,
        IListingFilterService filterService,
        INotificationSender notificationSender,
        CancellationToken cancellationToken)
    {
        var candidates = new List<Listing>();
        foreach (var source in listingSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = await source.SearchAsync(watch, cancellationToken).ConfigureAwait(false);
            candidates.AddRange(results);
        }

        var alreadySeen = await dbContext.Listings
            .Where(l => l.MatchedWatchId == watch.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var accepted = filterService.FilterAndDeduplicate(candidates, alreadySeen, watch);
        if (accepted.Count == 0)
        {
            return;
        }

        foreach (var listing in accepted)
        {
            listing.MatchedWatchId = watch.Id;
            if (listing.CreatedAt == default)
            {
                listing.CreatedAt = DateTime.UtcNow;
            }
        }

        dbContext.Listings.AddRange(accepted);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (watch.Client is null)
        {
            _logger.LogWarning("Watch {WatchId} has no associated Client; skipping notifications", watch.Id);
            return;
        }

        foreach (var listing in accepted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await notificationSender.SendListingNotificationAsync(watch.Client, listing, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A failed notification for one Listing must not stop notifications for the
                // remaining new Listings (for this Watch or later Watches in the same cycle).
                _logger.LogError(ex, "Error sending notification for listing {ListingId} (watch {WatchId})", listing.Id, watch.Id);
            }
        }
    }
}
