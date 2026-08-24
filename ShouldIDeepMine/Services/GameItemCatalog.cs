using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace ShouldIDeepMine.Services;

public sealed class GameItemCatalog
{
    private readonly IDataManager data;
    private IReadOnlyList<CategoryInfo>? categories;
    private Dictionary<uint, List<uint>>? categoryItems;

    public GameItemCatalog(IDataManager data) => this.data = data;

    public bool IsMarketable(uint itemId)
    {
        var sheet = data.GetExcelSheet<Item>();
        return sheet.TryGetRow(itemId, out var row) && row.ItemSearchCategory.RowId > 0;
    }

    public string GetName(uint itemId)
    {
        var sheet = data.GetExcelSheet<Item>();
        return sheet.TryGetRow(itemId, out var row) && !string.IsNullOrWhiteSpace(row.Name.ToString())
            ? row.Name.ToString()
            : $"Item #{itemId}";
    }

    public IReadOnlyList<CategoryInfo> GetCategories()
    {
        EnsureCategoryIndex();
        return categories!;
    }

    public IReadOnlyList<uint> GetMarketableItemIdsForCategory(uint categoryId, int limit)
    {
        EnsureCategoryIndex();
        if (!categoryItems!.TryGetValue(categoryId, out var ids))
            return Array.Empty<uint>();
        return ids.Take(Math.Clamp(limit, 1, 5000)).ToList();
    }

    private void EnsureCategoryIndex()
    {
        if (categories is not null)
            return;

        categoryItems = new Dictionary<uint, List<uint>>();
        foreach (var row in data.GetExcelSheet<Item>())
        {
            if (row.RowId == 0 || row.ItemSearchCategory.RowId == 0 || row.ItemUICategory.RowId == 0)
                continue;
            if (!categoryItems.TryGetValue(row.ItemUICategory.RowId, out var list))
                categoryItems[row.ItemUICategory.RowId] = list = new List<uint>();
            list.Add(row.RowId);
        }

        var names = data.GetExcelSheet<ItemUICategory>()
            .Where(x => x.RowId != 0 && !string.IsNullOrWhiteSpace(x.Name.ToString()))
            .ToDictionary(x => x.RowId, x => x.Name.ToString());

        categories = categoryItems
            .Where(x => names.ContainsKey(x.Key))
            .Select(x => new CategoryInfo(x.Key, names[x.Key], x.Value.Count))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
