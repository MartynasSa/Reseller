namespace Application.Models.Polling;

/// <summary>
/// Configuration for <c>WatchPollingWorker</c>, the background service that periodically runs
/// every active <see cref="Domain.Entities.Watch"/> against its configured
/// <see cref="Ports.IListingSource"/>s. Bound from configuration section "Polling".
/// </summary>
public class PollingOptions
{
    // 5 minutes is the v0 default (see TASKS.md "tune per plan tier later" note) - a low-touch
    // starting point until per-tier polling cadence is built.
    public int IntervalMinutes { get; set; } = 5;
}
