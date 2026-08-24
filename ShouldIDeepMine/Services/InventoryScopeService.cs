using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace ShouldIDeepMine.Services;

public sealed unsafe class InventoryScopeService
{
    public const string ShouldIOwnedIdsChannel = "ShouldI.ExternalMarketData.GetOwnedMarketableItemIds.v1";
    public const string ShouldIListingIdsChannel = "ShouldI.ExternalMarketData.GetCurrentListingItemIds.v1";

    private static readonly InventoryType[] PlayerInventory =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    private static readonly InventoryType[] Saddlebags =
    [
        InventoryType.SaddleBag1,
        InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1,
        InventoryType.PremiumSaddleBag2,
    ];

    private static readonly InventoryType[] RetainerInventory =
    [
        InventoryType.RetainerPage1,
        InventoryType.RetainerPage2,
        InventoryType.RetainerPage3,
        InventoryType.RetainerPage4,
        InventoryType.RetainerPage5,
        InventoryType.RetainerPage6,
        InventoryType.RetainerPage7,
    ];

    private readonly GameItemCatalog catalog;
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<string> ownedSubscriber;
    private readonly ICallGateSubscriber<string> listingSubscriber;

    public InventoryScopeService(IDalamudPluginInterface pluginInterface, GameItemCatalog catalog, IPluginLog log)
    {
        this.catalog = catalog;
        this.log = log;
        ownedSubscriber = pluginInterface.GetIpcSubscriber<string>(ShouldIOwnedIdsChannel);
        listingSubscriber = pluginInterface.GetIpcSubscriber<string>(ShouldIListingIdsChannel);
    }

    public bool ShouldIAvailable { get; private set; }

    public IReadOnlyList<uint> GetShouldIKnownOwned()
        => TryGetShouldIIds(ownedSubscriber, out var ids) ? ids : GetAllLoaded();

    public IReadOnlyList<uint> GetShouldIKnownListings()
        => TryGetShouldIIds(listingSubscriber, out var ids) ? ids : GetActiveRetainerListings();

    public IReadOnlyList<uint> GetPlayerInventory(bool includeSaddlebags)
    {
        var result = ReadContainers(PlayerInventory);
        if (includeSaddlebags)
            result.UnionWith(ReadContainers(Saddlebags));
        return result.Order().ToList();
    }

    public IReadOnlyList<uint> GetActiveRetainerInventory()
        => ReadContainers(RetainerInventory).Order().ToList();

    public IReadOnlyList<uint> GetActiveRetainerListings()
        => ReadContainers([InventoryType.RetainerMarket]).Order().ToList();

    public IReadOnlyList<uint> GetAllLoaded()
    {
        var result = ReadContainers(PlayerInventory);
        result.UnionWith(ReadContainers(Saddlebags));
        result.UnionWith(ReadContainers(RetainerInventory));
        result.UnionWith(ReadContainers([InventoryType.RetainerMarket]));
        return result.Order().ToList();
    }

    private bool TryGetShouldIIds(ICallGateSubscriber<string> subscriber, out IReadOnlyList<uint> ids)
    {
        try
        {
            var json = subscriber.InvokeFunc();
            var parsed = JsonSerializer.Deserialize<List<uint>>(json) ?? new List<uint>();
            ids = parsed.Where(x => x != 0 && catalog.IsMarketable(x)).Distinct().Order().ToList();
            ShouldIAvailable = true;
            return true;
        }
        catch (IpcNotReadyError)
        {
            ShouldIAvailable = false;
        }
        catch (Exception ex)
        {
            ShouldIAvailable = false;
            log.Debug(ex, "Could not read a Should I? scan scope over IPC.");
        }
        ids = Array.Empty<uint>();
        return false;
    }

    private HashSet<uint> ReadContainers(IEnumerable<InventoryType> types)
    {
        var output = new HashSet<uint>();
        try
        {
            var manager = InventoryManager.Instance();
            if (manager == null)
                return output;

            foreach (var type in types)
            {
                var container = manager->GetInventoryContainer(type);
                if (container == null || !container->IsLoaded)
                    continue;
                for (var slot = 0; slot < container->Size; slot++)
                {
                    var item = container->GetInventorySlot(slot);
                    if (item == null || item->ItemId == 0 || item->Quantity <= 0 || item->IsSymbolic)
                        continue;
                    var itemId = item->GetBaseItemId();
                    if (itemId == 0)
                        itemId = item->ItemId;
                    if (catalog.IsMarketable(itemId))
                        output.Add(itemId);
                }
            }
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Could not enumerate one or more loaded inventory containers.");
        }
        return output;
    }
}
