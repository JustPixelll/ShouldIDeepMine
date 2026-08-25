using Dalamud.Plugin.Services;

namespace ShouldIDeepMine.Services;

/// <summary>
/// Builds small, understandable native-query plans. The planner intentionally avoids complicated
/// optimization: Should I? supplies ranked candidates, Deep Mine deduplicates them, skips recent
/// native snapshots, applies the user's request budget, and leaves explicit/full scans untouched.
/// </summary>
public sealed class SmartScanPlanner
{
    private readonly Configuration configuration;
    private readonly IPlayerState playerState;
    private readonly InventoryScopeService scopes;
    private readonly GameItemCatalog catalog;
    private readonly DeepMinePublisher publisher;

    public SmartScanPlanner(
        Configuration configuration,
        IPlayerState playerState,
        InventoryScopeService scopes,
        GameItemCatalog catalog,
        DeepMinePublisher publisher)
    {
        this.configuration = configuration;
        this.playerState = playerState;
        this.scopes = scopes;
        this.catalog = catalog;
        this.publisher = publisher;
    }

    public DeepMineScanPlan BuildSmart(DeepMineSmartModule module)
    {
        var candidates = scopes.GetSmartCandidates(module);
        var candidateCount = candidates.Count;
        var duplicateCount = Math.Max(0, candidateCount - candidates.Select(x => x.ItemId).Distinct().Count());
        var worldId = playerState.CurrentWorld.RowId;
        var freshAge = TimeSpan.FromMinutes(Math.Max(1, configuration.SmartNativeFreshMinutes));
        var freshSkipped = 0;

        var selected = new List<DeepMineQueueItem>();
        foreach (var candidate in candidates
                     .GroupBy(x => x.ItemId)
                     .Select(g => g.OrderByDescending(x => x.Priority).First())
                     .OrderByDescending(x => x.Priority)
                     .ThenByDescending(x => x.OpportunityScore ?? 0))
        {
            if (publisher.IsFresh(worldId, candidate.ItemId, freshAge))
            {
                freshSkipped++;
                continue;
            }

            selected.Add(new DeepMineQueueItem(
                candidate.ItemId,
                string.IsNullOrWhiteSpace(candidate.ItemName) ? catalog.GetName(candidate.ItemId) : candidate.ItemName,
                Priority: candidate.Priority,
                Module: candidate.Module,
                Reason: candidate.Reason));

            if (selected.Count >= Math.Max(1, configuration.SmartQueryBudget))
                break;
        }

        return new DeepMineScanPlan(
            $"Should I? smart {DisplayName(module)}",
            module,
            candidateCount,
            freshSkipped,
            duplicateCount,
            selected,
            DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<DeepMineQueueItem> BuildStaleScope(
        IEnumerable<uint> itemIds,
        string module,
        TimeSpan age,
        bool includeNeverScanned)
    {
        if (!playerState.IsLoaded)
            return Array.Empty<DeepMineQueueItem>();

        var worldId = playerState.CurrentWorld.RowId;
        var output = new List<DeepMineQueueItem>();
        foreach (var itemId in itemIds.Where(x => x != 0 && catalog.IsMarketable(x)).Distinct())
        {
            var observed = publisher.GetObservedAt(worldId, itemId);
            if (observed is null && !includeNeverScanned)
                continue;
            if (observed is not null && DateTimeOffset.UtcNow - observed.Value <= age)
                continue;

            output.Add(new DeepMineQueueItem(
                itemId,
                catalog.GetName(itemId),
                Priority: observed is null ? 100 : 50,
                Module: module,
                Reason: observed is null
                    ? "No cached native snapshot exists for this item."
                    : $"Cached native snapshot is {(DateTimeOffset.UtcNow - observed.Value).TotalHours:0.#} hours old."));
        }

        return output
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.ItemName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<DeepMineQueueItem> BuildStaleCached(TimeSpan age)
    {
        if (!playerState.IsLoaded)
            return Array.Empty<DeepMineQueueItem>();

        var worldId = playerState.CurrentWorld.RowId;
        return publisher.GetAll(worldId)
            .Select(x => (Snapshot: x, Observed: x.ListingObservedAtUtc ?? x.HistoryObservedAtUtc ?? DateTimeOffset.MinValue))
            .Where(x => DateTimeOffset.UtcNow - x.Observed > age)
            .OrderBy(x => x.Observed)
            .Select(x => new DeepMineQueueItem(
                x.Snapshot.ItemId,
                catalog.GetName(x.Snapshot.ItemId),
                Priority: 50,
                Module: "Stale",
                Reason: $"Cached native snapshot is {(DateTimeOffset.UtcNow - x.Observed).TotalHours:0.#} hours old."))
            .ToList();
    }

    private static string DisplayName(DeepMineSmartModule module) => module switch
    {
        DeepMineSmartModule.Total => "total",
        DeepMineSmartModule.Sell => "sell",
        DeepMineSmartModule.BuyMB => "buy MB",
        DeepMineSmartModule.BuyVendor => "buy vendor",
        DeepMineSmartModule.Craft => "craft",
        DeepMineSmartModule.Gather => "gather",
        _ => module.ToString(),
    };
}
