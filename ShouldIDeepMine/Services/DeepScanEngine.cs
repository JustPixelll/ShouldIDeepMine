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

    public void Start(IEnumerable<uint> itemIds, string label)
    {
        if (IsRunning || !playerState.IsLoaded)
            return;
        queue.Clear();
        current = null;
        CompletedCount = 0;
        FailedCount = 0;

        foreach (var itemId in itemIds.Where(x => x != 0).Distinct())
        {
            if (catalog.IsMarketable(itemId))
                queue.Enqueue(new DeepMineQueueItem(itemId, catalog.GetName(itemId)));
        }
        InitialCount = queue.Count;
        if (InitialCount == 0)
        {
            State = DeepMineState.Completed;
            Status = $"No marketable items found for {label}.";
            return;
        }
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
                        Status = $"Done. {CompletedCount:N0} refreshed, {FailedCount:N0} failed.";
                        return;
                    }
                    current = queue.Dequeue();
                }
                SendCurrent(now);
                break;
            case DeepMineState.WaitingForPackets:
                if (currentHasHistory && now - lastPacketAt >= TimeSpan.FromMilliseconds(850))
                {
                    var snapshot = observer.GetSnapshot(playerState.CurrentWorld.RowId, current!.ItemId);
                    if (snapshot is not null)
                        publisher.Publish(snapshot);
                    CompletedCount++;
                    Status = $"Refreshed {current.ItemName}. {Remaining - 1:N0} remaining.";
                    current = null;
                    State = DeepMineState.Cooldown;
                    nextRequestAt = now.AddMilliseconds(Math.Max(1000, configuration.RequestSpacingMs));
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
            lastPacketAt = now;
            proxy->EntryCount = 0;
            proxy->SearchItemId = current.ItemId;
            if (!proxy->RequestData())
            {
                Status = $"Client refused request for {current.ItemName}; retrying.";
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
    }

    private void HandleTimeout(DateTimeOffset now)
    {
        if (current is null)
            return;
        if (current.Attempts < Math.Max(1, configuration.MaxRetries))
        {
            State = DeepMineState.Cooldown;
            nextRequestAt = now.AddMilliseconds(Math.Max(2000, configuration.RequestSpacingMs));
            Status = $"No complete response for {current.ItemName}; retry {current.Attempts + 1:N0}/{configuration.MaxRetries:N0}.";
            return;
        }
        FailedCount++;
        Status = $"Skipped {current.ItemName} after {current.Attempts:N0} failed attempt(s).";
        current = null;
        State = DeepMineState.Cooldown;
        nextRequestAt = now.AddMilliseconds(Math.Max(2000, configuration.RequestSpacingMs));
    }
}
