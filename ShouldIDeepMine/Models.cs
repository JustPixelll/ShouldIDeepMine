namespace ShouldIDeepMine;

public sealed record DeepMineListingDto(
    uint PricePerUnit,
    uint Quantity,
    bool IsHq,
    ulong ListingId,
    ulong RetainerId,
    string? RetainerName);

public sealed record DeepMineSaleDto(
    uint PricePerUnit,
    uint Quantity,
    bool IsHq,
    DateTimeOffset SoldAtUtc);

public sealed record DeepMineSnapshotDto(
    uint WorldId,
    uint ItemId,
    DateTimeOffset? ListingObservedAtUtc,
    DateTimeOffset? HistoryObservedAtUtc,
    List<DeepMineListingDto> Listings,
    List<DeepMineSaleDto> Sales);

public sealed record DeepMineQueueItem(uint ItemId, string ItemName, int Attempts = 0);
public sealed record CategoryInfo(uint CategoryId, string Name, int ItemCount);

public enum DeepMineState
{
    Idle,
    WaitingToRequest,
    WaitingForPackets,
    Cooldown,
    Completed,
    Stopped,
}
