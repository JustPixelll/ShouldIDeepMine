using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace ShouldIDeepMine.Services;

public sealed class DeepMinePublisher : IDisposable
{
    public const string SnapshotUpdatedChannel = "ShouldI.ExternalMarketData.SnapshotUpdated.v1";
    public const string GetSnapshotsChannel = "ShouldI.ExternalMarketData.GetSnapshots.v1";

    private readonly string path;
    private readonly IPluginLog log;
    private readonly ICallGateProvider<string, object> updatedProvider;
    private readonly ICallGateProvider<string> snapshotsProvider;
    private readonly Dictionary<string, DeepMineSnapshotDto> snapshots;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public DeepMinePublisher(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        Directory.CreateDirectory(pluginInterface.ConfigDirectory.FullName);
        path = Path.Combine(pluginInterface.ConfigDirectory.FullName, "deep-mine-cache.json");
        snapshots = Load();
        updatedProvider = pluginInterface.GetIpcProvider<string, object>(SnapshotUpdatedChannel);
        snapshotsProvider = pluginInterface.GetIpcProvider<string>(GetSnapshotsChannel);
        snapshotsProvider.RegisterFunc(() => JsonSerializer.Serialize(snapshots.Values.ToList(), JsonOptions));
    }

    public int CachedCount => snapshots.Count;

    public DeepMineSnapshotDto? Get(uint worldId, uint itemId)
        => snapshots.TryGetValue(Key(worldId, itemId), out var snapshot) ? snapshot : null;

    public IReadOnlyList<DeepMineSnapshotDto> GetAll(uint worldId = 0)
        => snapshots.Values
            .Where(x => worldId == 0 || x.WorldId == worldId)
            .OrderByDescending(x => ObservedAt(x))
            .ToList();

    public DateTimeOffset? GetObservedAt(uint worldId, uint itemId)
        => Get(worldId, itemId) is { } snapshot ? ObservedAt(snapshot) : null;

    public bool IsFresh(uint worldId, uint itemId, TimeSpan maximumAge)
    {
        var observed = GetObservedAt(worldId, itemId);
        return observed is not null && DateTimeOffset.UtcNow - observed.Value <= maximumAge;
    }

    public void Publish(DeepMineSnapshotDto snapshot)
    {
        if (snapshot.WorldId == 0 || snapshot.ItemId == 0)
            return;
        snapshots[Key(snapshot.WorldId, snapshot.ItemId)] = snapshot;
        Save();
        updatedProvider.SendMessage(JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    public void Dispose()
    {
        snapshotsProvider.UnregisterFunc();
        Save();
    }

    private Dictionary<string, DeepMineSnapshotDto> Load()
    {
        try
        {
            if (!File.Exists(path))
                return new();
            var list = JsonSerializer.Deserialize<List<DeepMineSnapshotDto>>(File.ReadAllText(path), JsonOptions) ?? new();
            return list.Where(x => x.WorldId != 0 && x.ItemId != 0).ToDictionary(x => Key(x.WorldId, x.ItemId));
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Could not load Deep Mine cache; starting clean.");
            return new();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(snapshots.Values.OrderByDescending(ObservedAt).ToList(), JsonOptions));
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Could not save Deep Mine cache.");
        }
    }

    private static DateTimeOffset ObservedAt(DeepMineSnapshotDto snapshot)
        => snapshot.ListingObservedAtUtc ?? snapshot.HistoryObservedAtUtc ?? DateTimeOffset.MinValue;

    private static string Key(uint worldId, uint itemId) => $"{worldId}:{itemId}";
}
