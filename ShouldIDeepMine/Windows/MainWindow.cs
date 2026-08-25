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
    private string updateListName = string.Empty;
    private string updateListIds = string.Empty;
    private string? editingListId;
    private string librarySearch = string.Empty;
    private bool fullMarketConfirmed;
    private bool includeNeverScanned = true;
    private DeepMineScanPlan? smartPlan;
    private IReadOnlyList<DeepMineQueueItem> stalePlan = Array.Empty<DeepMineQueueItem>();
    private string stalePlanLabel = string.Empty;

    public MainWindow(Plugin plugin) : base("Should I Deep Mine?##ShouldIDeepMine")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(900, 650),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Should I Deep Mine?");
        ImGui.TextWrapped("EXPERIMENTAL native Market Board evidence workstation. Smart scans use Should I?'s already-ranked opportunities, while full scans, stale-data maintenance and custom update lists remain explicit user-started scopes.");
        ImGui.TextDisabled(plugin.Scopes.SmartCandidatesAvailable
            ? $"Should I? smart link detected • {plugin.Publisher.CachedCount:N0} cached snapshot(s)."
            : plugin.Scopes.ShouldIAvailable
                ? $"Should I? basic link detected • {plugin.Publisher.CachedCount:N0} cached snapshot(s). Update Should I? for module-aware smart candidates."
                : $"Should I? link not yet detected • {plugin.Publisher.CachedCount:N0} cached snapshot(s). Manual/full scopes still work.");
        ImGui.Separator();

        DrawEngineStatus();

        if (ImGui.BeginTabBar("##deepmine-main-tabs"))
        {
            if (ImGui.BeginTabItem("Dashboard"))
            {
                DrawDashboard();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Smart Scan"))
            {
                DrawSmartScan();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Full Scan"))
            {
                DrawFullScan();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Stale Data"))
            {
                DrawStaleData();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Update Lists"))
            {
                DrawUpdateLists();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Data Library"))
            {
                DrawDataLibrary();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Settings"))
            {
                DrawTechnicalSettings();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
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
            {
                ImGui.TextDisabled($"Current: {current.ItemName} (#{current.ItemId}) • {current.Module} • attempt {current.Attempts:N0}");
                if (!string.IsNullOrWhiteSpace(current.Reason))
                    ImGui.TextWrapped($"Why: {current.Reason}");
            }
        }
        else
        {
            ImGui.TextDisabled(engine.Status);
            if (engine.LastCompletedAtUtc is { } completed)
                ImGui.TextDisabled($"Last completed: {engine.LastScope} • {completed.ToLocalTime():g}");
        }
        ImGui.Spacing();
    }

    private void DrawDashboard()
    {
        var worldId = Plugin.PlayerState.CurrentWorld.RowId;
        var cached = plugin.Publisher.GetAll(worldId);
        var now = DateTimeOffset.UtcNow;
        var freshAge = TimeSpan.FromMinutes(Math.Max(1, plugin.Configuration.SmartNativeFreshMinutes));
        var staleAge = TimeSpan.FromHours(Math.Max(1, plugin.Configuration.StaleAgeHours));
        var fresh = cached.Count(x => now - ObservedAt(x) <= freshAge);
        var stale = cached.Count(x => now - ObservedAt(x) > staleAge);

        ImGui.TextUnformatted("Market data health");
        ImGui.Text($"Native snapshots on current world: {cached.Count:N0}");
        ImGui.Text($"Smart-fresh (≤ {plugin.Configuration.SmartNativeFreshMinutes:N0} min): {fresh:N0}");
        ImGui.Text($"Stale (> {plugin.Configuration.StaleAgeHours:N0} h): {stale:N0}");
        ImGui.Text($"Should I? smart candidate IPC: {(plugin.Scopes.SmartCandidatesAvailable ? "ready" : "not detected yet")}");
        ImGui.Separator();

        ImGui.TextUnformatted("Recommended starting point");
        ImGui.TextWrapped("Build a Total smart plan. This does not try to mathematically squeeze every last request out of the queue: it uses Should I?'s rankings, removes duplicates, skips recent native snapshots and respects your request budget.");
        if (ImGui.Button("BUILD TOTAL SMART PLAN"))
            smartPlan = plugin.Planner.BuildSmart(DeepMineSmartModule.Total);
        ImGui.SameLine();
        if (smartPlan is { Module: DeepMineSmartModule.Total } && !plugin.Engine.IsRunning && smartPlan.Items.Count > 0)
        {
            if (ImGui.Button("START TOTAL SMART SCAN"))
                plugin.Engine.Start(smartPlan.Items, smartPlan.Label);
        }
        DrawSmartPlanSummary(DeepMineSmartModule.Total, compact: true);
        ImGui.Separator();

        ImGui.TextUnformatted("Quick full scopes");
        ScopeButton("ALL OWNED", () => plugin.Scopes.GetShouldIKnownOwned(), "Should I known owned items");
        ImGui.SameLine();
        ScopeButton("CURRENT LISTINGS", () => plugin.Scopes.GetShouldIKnownListings(), "Should I known current listings");
        ImGui.SameLine();
        ScopeButton("ALL LOADED", () => plugin.Scopes.GetAllLoaded(), "all currently loaded inventory containers");
    }

    private void DrawSmartScan()
    {
        var c = plugin.Configuration;
        ImGui.TextWrapped("Smart scans are deliberately simple: Should I? supplies ranked candidates; Deep Mine deduplicates them, skips recent native snapshots, and takes up to your request budget. Build a plan first so you can inspect exactly what would be queried.");

        var budget = c.SmartQueryBudget;
        if (ImGui.SliderInt("Request budget", ref budget, 5, 500))
        {
            c.SmartQueryBudget = budget;
            c.Save();
            smartPlan = null;
        }
        var freshness = c.SmartNativeFreshMinutes;
        if (ImGui.SliderInt("Skip native snapshots newer than (min)", ref freshness, 5, 1440))
        {
            c.SmartNativeFreshMinutes = freshness;
            c.Save();
            smartPlan = null;
        }
        ImGui.Separator();

        if (ImGui.BeginTabBar("##smart-subtabs"))
        {
            DrawSmartTab("Total", DeepMineSmartModule.Total,
                "Cross-module verification. Best default: one item queried once even if Sell, Buy, Craft and Gather all care about it.");
            DrawSmartTab("Sell", DeepMineSmartModule.Sell,
                "Prioritizes current listings and Should I?'s strongest owned-item sell candidates.");
            DrawSmartTab("Buy MB", DeepMineSmartModule.BuyMB,
                "Verifies Should I Buy?'s current market-board opportunities before you commit gil.");
            DrawSmartTab("Buy Vendor", DeepMineSmartModule.BuyVendor,
                "Verifies the market exit side of vendor-to-market opportunities; vendor acquisition prices are static game data.");
            DrawSmartTab("Craft", DeepMineSmartModule.Craft,
                "Checks top craft outputs plus a small number of the largest Market Board input-cost drivers.");
            DrawSmartTab("Gather", DeepMineSmartModule.Gather,
                "Checks the market value behind Should I Gather?'s strongest current gathering opportunities.");
            ImGui.EndTabBar();
        }
    }

    private void DrawSmartTab(string title, DeepMineSmartModule module, string description)
    {
        if (!ImGui.BeginTabItem(title))
            return;

        ImGui.TextWrapped(description);
        if (ImGui.Button($"BUILD {title.ToUpperInvariant()} PLAN##smart-{module}"))
            smartPlan = plugin.Planner.BuildSmart(module);
        ImGui.SameLine();
        var canStart = smartPlan is not null && smartPlan.Module == module && smartPlan.Items.Count > 0 && !plugin.Engine.IsRunning;
        if (!canStart)
            ImGui.BeginDisabled();
        if (ImGui.Button($"START PLAN##smart-start-{module}") && smartPlan is not null)
            plugin.Engine.Start(smartPlan.Items, smartPlan.Label);
        if (!canStart)
            ImGui.EndDisabled();

        DrawSmartPlanSummary(module, compact: false);
        ImGui.EndTabItem();
    }

    private void DrawSmartPlanSummary(DeepMineSmartModule module, bool compact)
    {
        if (smartPlan is null || smartPlan.Module != module)
            return;

        ImGui.Spacing();
        ImGui.Text($"Candidates from Should I?: {smartPlan.CandidateCount:N0}");
        ImGui.Text($"Duplicate item references removed: {smartPlan.DuplicateCount:N0}");
        ImGui.Text($"Recent native snapshots skipped: {smartPlan.FreshSkippedCount:N0}");
        ImGui.Text($"Native requests in plan: {smartPlan.Items.Count:N0}");

        if (compact)
            return;

        ImGui.Separator();
        ImGui.TextUnformatted("Plan preview");
        foreach (var item in smartPlan.Items.Take(30))
        {
            ImGui.Text($"{item.ItemName} (#{item.ItemId}) • {item.Module} • priority {item.Priority}");
            ImGui.TextWrapped($"  {item.Reason}");
        }
        if (smartPlan.Items.Count > 30)
            ImGui.TextDisabled($"...and {smartPlan.Items.Count - 30:N0} more item(s).");
    }

    private void DrawFullScan()
    {
        ImGui.TextWrapped("Full scans do exactly what they say. They do not apply smart freshness filtering or request budgets. Use them when coverage matters more than efficiency.");

        if (ImGui.BeginTabBar("##full-subtabs"))
        {
            if (ImGui.BeginTabItem("Owned & Inventory"))
            {
                ImGui.TextUnformatted("Should I? scopes");
                ScopeButton("ALL KNOWN OWNED", () => plugin.Scopes.GetShouldIKnownOwned(), "Should I known owned items");
                ImGui.SameLine();
                ScopeButton("CURRENT LISTINGS", () => plugin.Scopes.GetShouldIKnownListings(), "Should I known current listings");

                ImGui.TextUnformatted("Loaded client scopes");
                ScopeButton("PLAYER INVENTORY", () => plugin.Scopes.GetPlayerInventory(false), "loaded player inventory");
                ImGui.SameLine();
                ScopeButton("PLAYER + SADDLEBAGS", () => plugin.Scopes.GetPlayerInventory(true), "loaded player inventory and saddlebags");
                ScopeButton("ACTIVE RETAINER INVENTORY", () => plugin.Scopes.GetActiveRetainerInventory(), "active loaded retainer inventory");
                ImGui.SameLine();
                ScopeButton("ACTIVE RETAINER LISTINGS", () => plugin.Scopes.GetActiveRetainerListings(), "active loaded retainer listings");
                ScopeButton("ALL CURRENTLY LOADED CONTAINERS", () => plugin.Scopes.GetAllLoaded(), "all currently loaded inventory containers");
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Category"))
            {
                DrawCategoryScope();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Full Market"))
            {
                var all = plugin.Catalog.GetAllMarketableItemIds();
                ImGui.TextWrapped("Entire marketable FFXIV item catalog. This can be an extremely large native request queue and is intentionally never started automatically or by a smart scan.");
                ImGui.Text($"Current catalog scope: {all.Count:N0} marketable item IDs.");
                ImGui.Checkbox("I understand this is a very large explicit scan", ref fullMarketConfirmed);
                if (plugin.Engine.IsRunning || !fullMarketConfirmed)
                    ImGui.BeginDisabled();
                if (ImGui.Button("START FULL MARKET CATALOG SCAN"))
                {
                    plugin.Engine.Start(all, "entire marketable FFXIV item catalog");
                    fullMarketConfirmed = false;
                }
                if (plugin.Engine.IsRunning || !fullMarketConfirmed)
                    ImGui.EndDisabled();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Custom IDs"))
            {
                DrawCustomScope();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawStaleData()
    {
        var c = plugin.Configuration;
        ImGui.TextWrapped("Build maintenance queues from missing or old native snapshots. This is intentionally age-based and understandable rather than a complicated prediction model.");

        var hours = c.StaleAgeHours;
        if (ImGui.SliderInt("Stale after (hours)", ref hours, 1, 720))
        {
            c.StaleAgeHours = hours;
            c.Save();
            stalePlan = Array.Empty<DeepMineQueueItem>();
        }
        ImGui.Checkbox("Include items never scanned natively", ref includeNeverScanned);
        var age = TimeSpan.FromHours(Math.Max(1, c.StaleAgeHours));

        if (ImGui.Button("BUILD — OWNED STALE/MISSING"))
        {
            stalePlan = plugin.Planner.BuildStaleScope(plugin.Scopes.GetShouldIKnownOwned(), "Stale/Owned", age, includeNeverScanned);
            stalePlanLabel = "stale/missing Should I owned items";
        }
        ImGui.SameLine();
        if (ImGui.Button("BUILD — LISTINGS STALE/MISSING"))
        {
            stalePlan = plugin.Planner.BuildStaleScope(plugin.Scopes.GetShouldIKnownListings(), "Stale/Listings", age, includeNeverScanned);
            stalePlanLabel = "stale/missing current listings";
        }

        if (ImGui.Button("BUILD — SMART CANDIDATES STALE/MISSING"))
        {
            var ids = plugin.Scopes.GetSmartCandidates(DeepMineSmartModule.Total).Select(x => x.ItemId);
            stalePlan = plugin.Planner.BuildStaleScope(ids, "Stale/Smart", age, includeNeverScanned);
            stalePlanLabel = "stale/missing Should I smart candidates";
        }
        ImGui.SameLine();
        if (ImGui.Button("BUILD — ALL CACHED STALE"))
        {
            stalePlan = plugin.Planner.BuildStaleCached(age);
            stalePlanLabel = "all cached native snapshots older than threshold";
        }

        ImGui.Separator();
        ImGui.Text($"Maintenance plan: {stalePlan.Count:N0} request(s)");
        if (!string.IsNullOrWhiteSpace(stalePlanLabel))
            ImGui.TextDisabled(stalePlanLabel);
        var canStart = stalePlan.Count > 0 && !plugin.Engine.IsRunning;
        if (!canStart)
            ImGui.BeginDisabled();
        if (ImGui.Button("START STALE-DATA PLAN"))
            plugin.Engine.Start(stalePlan, stalePlanLabel);
        if (!canStart)
            ImGui.EndDisabled();

        foreach (var item in stalePlan.Take(30))
            ImGui.TextWrapped($"{item.ItemName} (#{item.ItemId}) — {item.Reason}");
        if (stalePlan.Count > 30)
            ImGui.TextDisabled($"...and {stalePlan.Count - 30:N0} more item(s).");
    }

    private void DrawUpdateLists()
    {
        ImGui.TextWrapped("Save reusable named item-ID lists for markets you care about repeatedly: raid consumables, housing, materia, personal flips, workshop materials, rare glamour, or anything else.");

        ImGui.SetNextItemWidth(320 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##update-list-name", "List name", ref updateListName, 96);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##update-list-ids", "Comma/space/newline-separated item IDs", ref updateListIds, 8192);

        var parsed = ParseIds(updateListIds);
        ImGui.TextDisabled($"{parsed.Count:N0} valid marketable unique item(s) in editor.");
        if (ImGui.Button(editingListId is null ? "SAVE NEW LIST" : "SAVE CHANGES"))
        {
            var name = string.IsNullOrWhiteSpace(updateListName) ? "Update list" : updateListName.Trim();
            if (editingListId is null)
            {
                plugin.Configuration.UpdateLists.Add(new DeepMineUpdateList
                {
                    Name = name,
                    ItemIds = parsed.ToList(),
                });
            }
            else
            {
                var existing = plugin.Configuration.UpdateLists.FirstOrDefault(x => x.Id == editingListId);
                if (existing is not null)
                {
                    existing.Name = name;
                    existing.ItemIds = parsed.ToList();
                }
            }
            plugin.Configuration.Save();
            editingListId = null;
            updateListName = string.Empty;
            updateListIds = string.Empty;
        }
        if (editingListId is not null)
        {
            ImGui.SameLine();
            if (ImGui.Button("CANCEL EDIT"))
            {
                editingListId = null;
                updateListName = string.Empty;
                updateListIds = string.Empty;
            }
        }

        ImGui.Separator();
        string? deleteId = null;
        foreach (var list in plugin.Configuration.UpdateLists.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            ImGui.Text($"{list.Name} — {list.ItemIds.Count:N0} item(s)");
            ImGui.SameLine();
            if (!plugin.Engine.IsRunning)
            {
                if (ImGui.Button($"RUN##run-list-{list.Id}"))
                    plugin.Engine.Start(list.ItemIds, $"update list: {list.Name}");
            }
            else
            {
                ImGui.BeginDisabled();
                ImGui.Button($"RUN##run-list-{list.Id}");
                ImGui.EndDisabled();
            }
            ImGui.SameLine();
            if (ImGui.Button($"EDIT##edit-list-{list.Id}"))
            {
                editingListId = list.Id;
                updateListName = list.Name;
                updateListIds = string.Join(", ", list.ItemIds);
            }
            ImGui.SameLine();
            if (ImGui.Button($"DELETE##delete-list-{list.Id}"))
                deleteId = list.Id;
        }

        if (deleteId is not null)
        {
            plugin.Configuration.UpdateLists.RemoveAll(x => x.Id == deleteId);
            plugin.Configuration.Save();
            if (editingListId == deleteId)
                editingListId = null;
        }
    }

    private void DrawDataLibrary()
    {
        if (!Plugin.PlayerState.IsLoaded)
        {
            ImGui.TextDisabled("Load into a character/world to browse world-specific cached snapshots.");
            return;
        }

        var worldId = Plugin.PlayerState.CurrentWorld.RowId;
        ImGui.SetNextItemWidth(360 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##library-search", "Search cached item name or ID...", ref librarySearch, 128);

        var rows = plugin.Publisher.GetAll(worldId)
            .Select(x => new { Snapshot = x, Name = plugin.Catalog.GetName(x.ItemId), Observed = ObservedAt(x) })
            .Where(x => string.IsNullOrWhiteSpace(librarySearch)
                        || x.Name.Contains(librarySearch, StringComparison.CurrentCultureIgnoreCase)
                        || x.Snapshot.ItemId.ToString().Contains(librarySearch, StringComparison.OrdinalIgnoreCase))
            .Take(150)
            .ToList();

        ImGui.TextDisabled($"Showing {rows.Count:N0} cached item(s), newest first (max 150 rows in this view).");
        foreach (var row in rows)
        {
            var age = DateTimeOffset.UtcNow - row.Observed;
            ImGui.Text($"{row.Name} (#{row.Snapshot.ItemId}) — {age.TotalHours:0.#}h old — {row.Snapshot.Listings.Count:N0} listing(s), {row.Snapshot.Sales.Count:N0} sale(s)");
            ImGui.SameLine();
            if (!plugin.Engine.IsRunning && ImGui.Button($"SCAN NOW##library-{row.Snapshot.ItemId}"))
                plugin.Engine.Start([row.Snapshot.ItemId], $"library refresh: {row.Name}");
        }
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
        ImGui.TextDisabled($"Category scans are capped at {plugin.Configuration.CategoryItemLimit:N0} items per run; change the cap in Settings.");
    }

    private void DrawCustomScope()
    {
        ImGui.TextUnformatted("Custom item IDs");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##custom-ids", "Comma/space/newline-separated item IDs", ref customIds, 8192);
        var ids = ParseIds(customIds);
        ImGui.TextDisabled($"{ids.Count:N0} valid marketable unique item(s).");
        if (plugin.Engine.IsRunning || ids.Count == 0)
            ImGui.BeginDisabled();
        if (ImGui.Button("SCAN CUSTOM IDS"))
            plugin.Engine.Start(ids, "custom item IDs");
        if (plugin.Engine.IsRunning || ids.Count == 0)
            ImGui.EndDisabled();
    }

    private void DrawTechnicalSettings()
    {
        ImGui.TextUnformatted("Native queue controls");
        var c = plugin.Configuration;
        var spacing = c.RequestSpacingMs;
        if (ImGui.SliderInt("Request spacing (ms)", ref spacing, 1500, 10000))
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
        if (ImGui.SliderInt("Max attempts", ref retries, 1, 5))
        {
            c.MaxRetries = retries;
            c.Save();
        }
        ImGui.TextDisabled("Retries back off automatically. A successful snapshot now waits for both history and offerings before publishing.");

        ImGui.Separator();
        ImGui.TextUnformatted("Smart scan defaults");
        var budget = c.SmartQueryBudget;
        if (ImGui.SliderInt("Smart request budget", ref budget, 5, 500))
        {
            c.SmartQueryBudget = budget;
            c.Save();
        }
        var freshness = c.SmartNativeFreshMinutes;
        if (ImGui.SliderInt("Native fresh window (min)", ref freshness, 5, 1440))
        {
            c.SmartNativeFreshMinutes = freshness;
            c.Save();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Full / maintenance scopes");
        var cap = c.CategoryItemLimit;
        if (ImGui.SliderInt("Category item cap", ref cap, 10, 5000))
        {
            c.CategoryItemLimit = cap;
            c.Save();
        }
        var staleHours = c.StaleAgeHours;
        if (ImGui.SliderInt("Default stale threshold (hours)", ref staleHours, 1, 720))
        {
            c.StaleAgeHours = staleHours;
            c.Save();
        }
        ImGui.TextDisabled("Deep Mine remains the explicit experimental boundary: Should I? can expose read-only candidate hints, but only you start a native scan here.");
    }

    private IReadOnlyList<uint> ParseIds(string input)
        => input.Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => uint.TryParse(x, out var id) ? id : 0)
            .Where(x => x != 0 && plugin.Catalog.IsMarketable(x))
            .Distinct()
            .ToList();

    private static DateTimeOffset ObservedAt(DeepMineSnapshotDto snapshot)
        => snapshot.ListingObservedAtUtc ?? snapshot.HistoryObservedAtUtc ?? DateTimeOffset.MinValue;
}
