using Dalamud.Game.Network.Structures;
using Dalamud.Plugin.Services;

namespace ShouldIDeepMine.Services;

public enum DeepMinePacketKind { History, Offerings }

public sealed class MarketBoardObserver : IDisposable
{
    private readonly IMarketBoard marketBoard;
    private readonly IPlayerState playerState;
    private readonly Dictionary<(uint WorldId, uint ItemId), DeepMineSnapshotDto> working = new();
    private uint lastHistoryItemId;
    private uint expectedItemId;

    public MarketBoardObserver(IMarketBoard marketBoard, IPlayerState playerState)
    {
        this.marketBoard = marketBoard;
        this.playerState = playerState;
        marketBoard.HistoryReceived += OnHistoryReceived;
        marketBoard.OfferingsReceived += OnOfferingsReceived;
    }

    public event Action<uint, DeepMinePacketKind>? PacketObserved;

    public void Expect(uint itemId) => expectedItemId = itemId;

    public DeepMineSnapshotDto? GetSnapshot(uint worldId, uint itemId)
        => working.TryGetValue((worldId, itemId), out var snapshot)
            ? snapshot with { Listings = snapshot.Listings.ToList(), Sales = snapshot.Sales.ToList() }
            : null;

    public void Dispose()
    {
        marketBoard.HistoryReceived -= OnHistoryReceived;
        marketBoard.OfferingsReceived -= OnOfferingsReceived;
    }

    private void OnHistoryReceived(IMarketBoardHistory history)
    {
        if (!playerState.IsLoaded || history.ItemId == 0)
            return;
        var worldId = playerState.CurrentWorld.RowId;
        var now = DateTimeOffset.UtcNow;
        lastHistoryItemId = history.ItemId;
        expectedItemId = history.ItemId;
        working[(worldId, history.ItemId)] = new DeepMineSnapshotDto(
            worldId,
            history.ItemId,
            now,
            now,
            new List<DeepMineListingDto>(),
            history.HistoryListings.Select(x => new DeepMineSaleDto(
                x.SalePrice,
                x.Quantity,
                x.IsHq,
                new DateTimeOffset(DateTime.SpecifyKind(x.PurchaseTime, DateTimeKind.Utc)))).ToList());
        PacketObserved?.Invoke(history.ItemId, DeepMinePacketKind.History);
    }

    private void OnOfferingsReceived(IMarketBoardCurrentOfferings offerings)
    {
        if (!playerState.IsLoaded)
            return;
        var rows = offerings.ItemListings;
        var itemId = rows.Count > 0 ? rows[0].ItemId : expectedItemId != 0 ? expectedItemId : lastHistoryItemId;
        if (itemId == 0)
            return;
        var worldId = playerState.CurrentWorld.RowId;
        var now = DateTimeOffset.UtcNow;
        if (!working.TryGetValue((worldId, itemId), out var snapshot))
            snapshot = new DeepMineSnapshotDto(worldId, itemId, now, null, new(), new());

        var byId = snapshot.Listings.Where(x => x.ListingId != 0).ToDictionary(x => x.ListingId);
        foreach (var row in rows)
        {
            var dto = new DeepMineListingDto(row.PricePerUnit, row.ItemQuantity, row.IsHq, row.ListingId, row.RetainerId, row.RetainerName);
            if (dto.ListingId != 0)
                byId[dto.ListingId] = dto;
            else
                snapshot.Listings.Add(dto);
        }
        snapshot = snapshot with
        {
            ListingObservedAtUtc = now,
            Listings = snapshot.Listings.Where(x => x.ListingId == 0).Concat(byId.Values).OrderBy(x => x.PricePerUnit).ToList(),
        };
        working[(worldId, itemId)] = snapshot;
        PacketObserved?.Invoke(itemId, DeepMinePacketKind.Offerings);
    }
}
