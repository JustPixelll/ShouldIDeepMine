using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ShouldIDeepMine.Services;
using ShouldIDeepMine.Windows;

namespace ShouldIDeepMine;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/deepmine";
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IMarketBoard MarketBoard { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; }
    public GameItemCatalog Catalog { get; }
    public InventoryScopeService Scopes { get; }
    public DeepMinePublisher Publisher { get; }
    public MarketBoardObserver Observer { get; }
    public SmartScanPlanner Planner { get; }
    public DeepScanEngine Engine { get; }
    public readonly WindowSystem WindowSystem = new("ShouldIDeepMine");
    private readonly MainWindow window;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Catalog = new GameItemCatalog(DataManager);
        Scopes = new InventoryScopeService(PluginInterface, Catalog, Log);
        Publisher = new DeepMinePublisher(PluginInterface, Log);
        Observer = new MarketBoardObserver(MarketBoard, PlayerState);
        Planner = new SmartScanPlanner(Configuration, PlayerState, Scopes, Catalog, Publisher);
        Engine = new DeepScanEngine(Configuration, Framework, PlayerState, Catalog, Observer, Publisher, Log);
        window = new MainWindow(this);
        WindowSystem.AddWindow(window);

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Should I Deep Mine?. Smart: smart [total|sell|buymb|buyvendor|craft|gather]. Full scopes: all, listings, inventory, saddlebags, retainer, retainerlistings, loaded, category <id/name>, items <ids>. Other: stop, status, help.",
        });
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += Open;
        PluginInterface.UiBuilder.OpenConfigUi += Open;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Open;
        PluginInterface.UiBuilder.OpenConfigUi -= Open;
        CommandManager.RemoveHandler(Command);
        Engine.Dispose();
        Observer.Dispose();
        Publisher.Dispose();
        WindowSystem.RemoveAllWindows();
    }

    private void OnCommand(string _, string args)
    {
        var trimmed = args.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            window.Toggle();
            return;
        }

        var split = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var verb = split[0].ToLowerInvariant();
        var remainder = split.Length > 1 ? split[1].Trim() : string.Empty;

        switch (verb)
        {
            case "smart":
            case "verify":
                StartSmart(remainder);
                return;
            case "all":
            case "owned":
                Start(Scopes.GetShouldIKnownOwned(), "Should I known owned items");
                return;
            case "listings":
            case "listed":
                Start(Scopes.GetShouldIKnownListings(), "Should I known current listings");
                return;
            case "inventory":
            case "inv":
                Start(Scopes.GetPlayerInventory(false), "loaded player inventory");
                return;
            case "saddlebags":
            case "saddlebag":
            case "bags":
                Start(Scopes.GetPlayerInventory(true), "loaded player inventory and saddlebags");
                return;
            case "retainer":
            case "retainerinventory":
                Start(Scopes.GetActiveRetainerInventory(), "active loaded retainer inventory");
                return;
            case "retainerlistings":
            case "retainer-listings":
                Start(Scopes.GetActiveRetainerListings(), "active loaded retainer listings");
                return;
            case "loaded":
            case "allloaded":
            case "all-loaded":
                Start(Scopes.GetAllLoaded(), "all currently loaded inventory containers");
                return;
            case "category":
            case "cat":
                StartCategory(remainder);
                return;
            case "items":
            case "item":
            case "ids":
                StartItems(remainder);
                return;
            case "stop":
            case "cancel":
                Engine.Stop();
                ChatGui.Print("[Should I Deep Mine?] Active scan stopped.");
                return;
            case "status":
                ChatGui.Print($"[Should I Deep Mine?] {Engine.Status}");
                window.IsOpen = true;
                return;
            case "help":
            case "?":
                PrintHelp();
                return;
            default:
                ChatGui.PrintError($"[Should I Deep Mine?] Unknown command '{verb}'. Use /deepmine help.");
                return;
        }
    }

    private void StartSmart(string input)
    {
        var module = input.Trim().ToLowerInvariant() switch
        {
            "" or "total" or "all" => DeepMineSmartModule.Total,
            "sell" => DeepMineSmartModule.Sell,
            "buymb" or "buy" or "market" => DeepMineSmartModule.BuyMB,
            "buyvendor" or "vendor" => DeepMineSmartModule.BuyVendor,
            "craft" or "crafter" => DeepMineSmartModule.Craft,
            "gather" or "gatherer" => DeepMineSmartModule.Gather,
            _ => (DeepMineSmartModule?)null,
        };

        if (module is null)
        {
            ChatGui.PrintError("[Should I Deep Mine?] Usage: /deepmine smart [total|sell|buymb|buyvendor|craft|gather]");
            return;
        }

        var plan = Planner.BuildSmart(module.Value);
        if (plan.Items.Count == 0)
        {
            ChatGui.Print($"[Should I Deep Mine?] Smart {module.Value}: nothing needs native verification within the current freshness/budget settings.");
            window.IsOpen = true;
            return;
        }

        Engine.Start(plan.Items, plan.Label);
        window.IsOpen = true;
        ChatGui.Print($"[Should I Deep Mine?] Started {plan.Label}: {plan.Items.Count:N0} request(s), {plan.FreshSkippedCount:N0} recent native snapshot(s) skipped.");
    }

    private void Start(IReadOnlyList<uint> ids, string scope)
    {
        if (Engine.IsRunning)
        {
            ChatGui.PrintError("[Should I Deep Mine?] A scan is already running. Use /deepmine stop first.");
            window.IsOpen = true;
            return;
        }

        if (ids.Count == 0)
        {
            ChatGui.PrintError($"[Should I Deep Mine?] No marketable items are currently available for scope: {scope}.");
            window.IsOpen = true;
            return;
        }

        Engine.Start(ids, scope);
        window.IsOpen = true;
        ChatGui.Print($"[Should I Deep Mine?] Started {scope}: {ids.Count:N0} unique marketable item(s).");
    }

    private void StartCategory(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ChatGui.PrintError("[Should I Deep Mine?] Usage: /deepmine category <category id or name>");
            return;
        }

        var categories = Catalog.GetCategories();
        CategoryInfo? selected = null;
        if (uint.TryParse(query, out var categoryId))
            selected = categories.FirstOrDefault(x => x.CategoryId == categoryId);
        else
        {
            selected = categories.FirstOrDefault(x => x.Name.Equals(query, StringComparison.CurrentCultureIgnoreCase))
                       ?? categories.Where(x => x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                           .OrderBy(x => x.Name.Length)
                           .FirstOrDefault();
        }

        if (selected is null)
        {
            ChatGui.PrintError($"[Should I Deep Mine?] No FFXIV item category matched '{query}'.");
            return;
        }

        var ids = Catalog.GetMarketableItemIdsForCategory(selected.CategoryId, Configuration.CategoryItemLimit);
        Start(ids, $"category: {selected.Name}");
    }

    private void StartItems(string input)
    {
        var ids = input.Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => uint.TryParse(x, out var id) ? id : 0)
            .Where(x => x != 0 && Catalog.IsMarketable(x))
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            ChatGui.PrintError("[Should I Deep Mine?] Usage: /deepmine items <item id> [more ids...]");
            return;
        }
        Start(ids, "custom item IDs");
    }

    private static void PrintHelp()
    {
        ChatGui.Print("[Should I Deep Mine?] /deepmine smart [total|sell|buymb|buyvendor|craft|gather] — build a small Should I?-guided verification plan");
        ChatGui.Print("[Should I Deep Mine?] /deepmine all — all marketable items Should I? currently knows you own");
        ChatGui.Print("[Should I Deep Mine?] /deepmine listings — items Should I? currently knows you have listed");
        ChatGui.Print("[Should I Deep Mine?] /deepmine inventory — loaded player inventory only");
        ChatGui.Print("[Should I Deep Mine?] /deepmine saddlebags — player inventory + loaded saddlebags");
        ChatGui.Print("[Should I Deep Mine?] /deepmine retainer — active retainer inventory");
        ChatGui.Print("[Should I Deep Mine?] /deepmine retainerlistings — active retainer listings");
        ChatGui.Print("[Should I Deep Mine?] /deepmine loaded — every currently loaded supported inventory container");
        ChatGui.Print("[Should I Deep Mine?] /deepmine category <id/name> — one FFXIV item UI category");
        ChatGui.Print("[Should I Deep Mine?] /deepmine items <ids...> — explicit item IDs");
        ChatGui.Print("[Should I Deep Mine?] /deepmine status | stop | help");
    }

    private void Open() => window.IsOpen = true;
}
