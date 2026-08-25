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

public sealed record DeepMineQueueItem(
    uint ItemId,
    string ItemName,
    int Attempts = 0,
    int Priority = 0,
    string Module = "Manual",
    string Reason = "Explicit scan scope");

public sealed record CategoryInfo(uint CategoryId, string Name, int ItemCount);

public enum DeepMineSmartModule
{
    Total,
    Sell,
    BuyMB,
    BuyVendor,
    Craft,
    Gather,
}

public sealed record SmartCandidateDto(
    string Module,
    uint ItemId,
    string ItemName,
    int Priority,
    string Reason,
    double? OpportunityScore,
    double? Confidence,
    DateTimeOffset? MarketFreshnessUtc);

public sealed record DeepMineScanPlan(
    string Label,
    DeepMineSmartModule Module,
    int CandidateCount,
    int FreshSkippedCount,
    int DuplicateCount,
    IReadOnlyList<DeepMineQueueItem> Items,
    DateTimeOffset BuiltAtUtc);

public enum DeepMineState
{
    Idle,
    WaitingToRequest,
    WaitingForPackets,
    Cooldown,
    Completed,
    Stopped,
}
