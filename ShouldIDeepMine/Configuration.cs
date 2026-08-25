using Dalamud.Configuration;

namespace ShouldIDeepMine;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    public int RequestSpacingMs { get; set; } = 2500;
    public int RequestTimeoutMs { get; set; } = 12000;
    public int MaxRetries { get; set; } = 2;
    public int CategoryItemLimit { get; set; } = 500;
    public int SmartQueryBudget { get; set; } = 75;
    public int SmartNativeFreshMinutes { get; set; } = 90;
    public int StaleAgeHours { get; set; } = 24;
    public List<DeepMineUpdateList> UpdateLists { get; set; } = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
