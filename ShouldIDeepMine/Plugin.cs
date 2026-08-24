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
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; }
    public GameItemCatalog Catalog { get; }
    public InventoryScopeService Scopes { get; }
    public DeepMinePublisher Publisher { get; }
    public MarketBoardObserver Observer { get; }
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
        Engine = new DeepScanEngine(Configuration, Framework, PlayerState, Catalog, Observer, Publisher, Log);
        window = new MainWindow(this);
        WindowSystem.AddWindow(window);

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Should I Deep Mine?. /deepmine stop stops the active queue.",
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
        if (args.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            Engine.Stop();
            return;
        }
        window.Toggle();
    }

    private void Open() => window.IsOpen = true;
}
