using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace ShouldIDeepMine.Services;

public sealed unsafe class DeepScanEngine : IDisposable
{
    private readonly Configuration configuration;
    private readonly IFramework framework;
    private readonly IPlayerState playerState;
    private readonly GameItemCatalog catalog;
    private readonly MarketBoardObserver observer;
    private readonly DeepMinePublisher publisher;
    private readonly IPluginLog log;
    private readonly Queue<DeepMineQueueItem> queue = new();
    private DeepMineQueueItem? current;
    private DateTimeOffset stateSince;
    private DateTimeOffset lastPacketAt;
    private DateTimeOffset nextRequestAt;
    private bool currentHasHistory;
    private bool currentHasOfferings;

    public DeepScanEngine(Configuration configuration, IFramework framework, IPlayerState playerState,
        GameItemCatalog catalog, MarketBoardObserver observer, DeepMinePublisher publisher, IPluginLog log)
    {
        this.configuration = configuration;
        this.framework = framework;
        this.playerState = playerState;
        this.catalog = catalog;
        this.observer = observer;
        this.publisher = publisher;
        this.log = log;
        observer.PacketObserved += OnPacketObserved;
        framework.Update += OnFrameworkUpdate;
    }

    public DeepMineState State { get; private set; } = DeepMineState.Idle;
    public string Status { get; private set; } = "Idle";
    public int InitialCount { get; private set; }
    public int CompletedCount { get; private set; }
    public int FailedCount { get; private set; }
    public int Remaining => queue.Count + (current is null ? 0 : 1);
    public DeepMineQueueItem? Current => current;
    public bool IsRunning => State is DeepMineState.WaitingToRequest or DeepMineState.WaitingForPackets or DeepMineState.Cooldown;
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? LastCompletedAtUtc { get; private set; }
    public string LastScope { get; private set; } = string.Empty;

    public void Start(IEnumerable<uint> itemIds, string label)
        => Start(itemIds.Select(id => new DeepMineQueueItem(id, catalog.GetName(id))), label);

    public void Start(IEnumerable<DeepMineQueueItem> items, string label)
    {
        if (IsRunning || !playerState.IsLoaded)
            return;

        queue.Clear();
        current = null;
        CompletedCount = 0;
        FailedCount = 0;
        LastScope = label;

        var distinct = items
            .Where(x => x.ItemId != 0 && catalog.IsMarketable(x.ItemId))
            .GroupBy(x => x.ItemId)
            .Select(g => g.OrderByDescending(x => x.Priority).First())
            .ToList();

        foreach (var item in distinct)
            queue.Enqueue(item with { ItemName = string.IsNullOrWhiteSpace(item.ItemName) ? catalog.GetName(item.ItemId) : item.ItemName, Attempts = 0 });

        InitialCount = queue.Count;
        if (InitialCount == 0)
        {
            State = DeepMineState.Completed;
            Status = $"No marketable items found for {label}.";
            return;
        }

        StartedAtUtc = DateTimeOffset.UtcNow;
        LastCompletedAtUtc = null;
        State = DeepMineState.WaitingToRequest;
        nextRequestAt = DateTimeOffset.UtcNow;
        stateSince = nextRequestAt;
        Status = $"Queued {InitialCount:N0} item(s): {label}.";
    }

    public void Stop(string reason = "Stopped by user.")
    {
        queue.Clear();
        current = null;
        State = DeepMineState.Stopped;
        Status = reason;
    }

    public void Dispose()
    {
        observer.PacketObserved -= OnPacketObserved;
        framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!IsRunning)
            return;
        if (!playerState.IsLoaded)
        {
            Stop("Stopped: player state unloaded.");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        switch (State)
        {
            case DeepMineState.WaitingToRequest:
                if (now < nextRequestAt)
                    return;
                if (current is null)
                {
                    if (queue.Count == 0)
                    {
                        State = DeepMineState.Completed;
                        LastCompletedAtUtc = now;
                        Status = $"Done. {CompletedCount:N0} refreshed, {FailedCount:N0} failed.";
                        return;
                    }
                    current = queue.Dequeue();
                }
                SendCurrent(now);
                break;

            case DeepMineState.WaitingForPackets:
                if (currentHasHistory && currentHasOfferings && now - lastPacketAt >= TimeSpan.FromMilliseconds(650))
                {
                    var snapshot = observer.GetSnapshot(playerState.CurrentWorld.RowId, current!.ItemId);
                    if (snapshot is not null)
                        publisher.Publish(snapshot);
                    CompletedCount++;
                    Status = $"Refreshed {current.ItemName}. {Remaining - 1:N0} remaining.";
                    current = null;
                    State = DeepMineState.Cooldown;
                    nextRequestAt = now.AddMilliseconds(Math.Max(1500, configuration.RequestSpacingMs));
                    return;
                }

                if (now - stateSince >= TimeSpan.FromMilliseconds(Math.Max(4000, configuration.RequestTimeoutMs)))
                    HandleTimeout(now);
                break;

            case DeepMineState.Cooldown:
                if (now >= nextRequestAt)
                    State = DeepMineState.WaitingToRequest;
                break;
        }
    }

    private void SendCurrent(DateTimeOffset now)
    {
        if (current is null)
            return;

        try
        {
            var proxy = InfoProxyItemSearch.Instance();
            if (proxy == null)
            {
                Status = "Waiting for ItemSearch info proxy...";
                nextRequestAt = now.AddSeconds(1);
                return;
            }

            current = current with { Attempts = current.Attempts + 1 };
            currentHasHistory = false;
            currentHasOfferings = false;
            lastPacketAt = now;
            proxy->EntryCount = 0;
            proxy->SearchItemId = current.ItemId;
            if (!proxy->RequestData())
            {
                Status = $"Client refused request for {current.ItemName}; backing off.";
                HandleTimeout(now);
                return;
            }

            State = DeepMineState.WaitingForPackets;
            stateSince = now;
            Status = $"Requesting {current.ItemName} ({CompletedCount + FailedCount + 1:N0}/{InitialCount:N0}), attempt {current.Attempts:N0}.";
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Deep Mine request failed for item {ItemId}.", current.ItemId);
            HandleTimeout(now);
        }
    }

    private void OnPacketObserved(uint itemId, DeepMinePacketKind kind)
    {
        if (State != DeepMineState.WaitingForPackets || current is null || current.ItemId != itemId)
            return;

        lastPacketAt = DateTimeOffset.UtcNow;
        if (kind == DeepMinePacketKind.History)
            currentHasHistory = true;
        if (kind == DeepMinePacketKind.Offerings)
            currentHasOfferings = true;
    }

    private void HandleTimeout(DateTimeOffset now)
    {
        if (current is null)
            return;

        if (current.Attempts < Math.Max(1, configuration.MaxRetries))
        {
            State = DeepMineState.Cooldown;
            var backoffMs = Math.Max(3000, configuration.RequestSpacingMs * (current.Attempts + 1));
            nextRequestAt = now.AddMilliseconds(backoffMs);
            Status = $"No complete response for {current.ItemName}; retry {current.Attempts + 1:N0}/{configuration.MaxRetries:N0} after backoff.";
            return;
        }

        FailedCount++;
        Status = $"Skipped {current.ItemName} after {current.Attempts:N0} failed attempt(s).";
        current = null;
        State = DeepMineState.Cooldown;
        nextRequestAt = now.AddMilliseconds(Math.Max(4000, configuration.RequestSpacingMs * 2));
    }
}
