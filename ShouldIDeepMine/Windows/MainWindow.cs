using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace ShouldIDeepMine.Windows;

public sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private uint selectedCategory;
    private string categorySearch = string.Empty;
    private string customIds = string.Empty;

    public MainWindow(Plugin plugin) : base("Should I Deep Mine?##ShouldIDeepMine")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760, 560),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Should I Deep Mine?");
        ImGui.TextWrapped("EXPERIMENTAL companion to Should I?. Nothing scans automatically on plugin load. You choose a scope, then Deep Mine walks that queue through FFXIV's native ItemSearch path and publishes completed snapshots to Should I? over IPC.");
        ImGui.TextDisabled(plugin.Scopes.ShouldIAvailable
            ? $"Should I? link detected • {plugin.Publisher.CachedCount:N0} cached snapshot(s)."
            : $"Should I? link not yet detected • {plugin.Publisher.CachedCount:N0} cached snapshot(s). Should I-scoped buttons fall back to currently loaded containers.");
        ImGui.Separator();

        DrawEngineStatus();
        DrawScanScopes();
        ImGui.Separator();
        DrawCategoryScope();
        ImGui.Separator();
        DrawCustomScope();
        ImGui.Separator();
        DrawTechnicalSettings();
    }

    private void DrawEngineStatus()
    {
        var engine = plugin.Engine;
        if (engine.IsRunning)
        {
            if (ImGui.Button("STOP ACTIVE SCAN"))
                engine.Stop();
            ImGui.SameLine();
            ImGui.TextDisabled(engine.Status);
            var progress = engine.InitialCount > 0
                ? Math.Clamp((engine.CompletedCount + engine.FailedCount) / (float)engine.InitialCount, 0, 1)
                : 0;
            ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{engine.CompletedCount + engine.FailedCount:N0}/{engine.InitialCount:N0}");
            if (engine.Current is { } current)
                ImGui.TextDisabled($"Current: {current.ItemName} (#{current.ItemId}) • attempt {current.Attempts:N0}");
        }
        else
        {
            ImGui.TextDisabled(engine.Status);
        }
        ImGui.Spacing();
    }

    private void DrawScanScopes()
    {
        ImGui.TextUnformatted("Quick scopes");
        ScopeButton("SHOULD I? — ALL KNOWN OWNED", () => plugin.Scopes.GetShouldIKnownOwned(), "Should I known owned items");
        ImGui.SameLine();
        ScopeButton("SHOULD I? — CURRENT LISTINGS", () => plugin.Scopes.GetShouldIKnownListings(), "Should I known current listings");

        ScopeButton("PLAYER INVENTORY", () => plugin.Scopes.GetPlayerInventory(false), "loaded player inventory");
        ImGui.SameLine();
        ScopeButton("PLAYER + SADDLEBAGS", () => plugin.Scopes.GetPlayerInventory(true), "loaded player inventory and saddlebags");

        ScopeButton("ACTIVE RETAINER INVENTORY", () => plugin.Scopes.GetActiveRetainerInventory(), "active loaded retainer inventory");
        ImGui.SameLine();
        ScopeButton("ACTIVE RETAINER LISTINGS", () => plugin.Scopes.GetActiveRetainerListings(), "active loaded retainer listings");

        ScopeButton("ALL CURRENTLY LOADED CONTAINERS", () => plugin.Scopes.GetAllLoaded(), "all currently loaded inventory containers");
    }

    private void ScopeButton(string label, Func<IReadOnlyList<uint>> getIds, string scope)
    {
        if (plugin.Engine.IsRunning)
            ImGui.BeginDisabled();
        if (ImGui.Button(label))
        {
            var ids = getIds();
            plugin.Engine.Start(ids, scope);
        }
        if (plugin.Engine.IsRunning)
            ImGui.EndDisabled();
    }

    private void DrawCategoryScope()
    {
        ImGui.TextUnformatted("FFXIV item category");
        var categories = plugin.Catalog.GetCategories();
        ImGui.SetNextItemWidth(280 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##category-search", "Filter categories...", ref categorySearch, 96);

        var current = categories.FirstOrDefault(x => x.CategoryId == selectedCategory);
        var preview = current is null ? "Choose category" : $"{current.Name} ({current.ItemCount:N0})";
        ImGui.SetNextItemWidth(360 * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("##category", preview))
        {
            foreach (var category in categories.Where(x => string.IsNullOrWhiteSpace(categorySearch) || x.Name.Contains(categorySearch, StringComparison.CurrentCultureIgnoreCase)))
            {
                if (ImGui.Selectable($"{category.Name} ({category.ItemCount:N0})##cat-{category.CategoryId}", selectedCategory == category.CategoryId))
                    selectedCategory = category.CategoryId;
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (plugin.Engine.IsRunning || selectedCategory == 0)
            ImGui.BeginDisabled();
        if (ImGui.Button("SCAN CATEGORY"))
        {
            var ids = plugin.Catalog.GetMarketableItemIdsForCategory(selectedCategory, plugin.Configuration.CategoryItemLimit);
            plugin.Engine.Start(ids, $"category: {current?.Name ?? selectedCategory.ToString()}");
        }
        if (plugin.Engine.IsRunning || selectedCategory == 0)
            ImGui.EndDisabled();
        ImGui.TextDisabled($"Category scans are capped at {plugin.Configuration.CategoryItemLimit:N0} items per run; change the cap below.");
    }

    private void DrawCustomScope()
    {
        ImGui.TextUnformatted("Custom item IDs");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##custom-ids", "Comma/space/newline-separated item IDs", ref customIds, 4096);
        if (plugin.Engine.IsRunning)
            ImGui.BeginDisabled();
        if (ImGui.Button("SCAN CUSTOM IDS"))
        {
            var ids = customIds.Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => uint.TryParse(x, out var id) ? id : 0)
                .Where(x => x != 0)
                .Distinct()
                .ToList();
            plugin.Engine.Start(ids, "custom item IDs");
        }
        if (plugin.Engine.IsRunning)
            ImGui.EndDisabled();
    }

    private void DrawTechnicalSettings()
    {
        ImGui.TextUnformatted("Queue controls");
        var c = plugin.Configuration;
        var spacing = c.RequestSpacingMs;
        if (ImGui.SliderInt("Request spacing (ms)", ref spacing, 1000, 10000))
        {
            c.RequestSpacingMs = spacing;
            c.Save();
        }
        var timeout = c.RequestTimeoutMs;
        if (ImGui.SliderInt("Response timeout (ms)", ref timeout, 4000, 30000))
        {
            c.RequestTimeoutMs = timeout;
            c.Save();
        }
        var retries = c.MaxRetries;
        if (ImGui.SliderInt("Max attempts", ref retries, 1, 6))
        {
            c.MaxRetries = retries;
            c.Save();
        }
        var cap = c.CategoryItemLimit;
        if (ImGui.SliderInt("Category item cap", ref cap, 10, 2000))
        {
            c.CategoryItemLimit = cap;
            c.Save();
        }
        ImGui.TextDisabled("These controls intentionally live here rather than in Should I?. Deep Mine is the experimental boundary for queued native data collection.");
    }
}
