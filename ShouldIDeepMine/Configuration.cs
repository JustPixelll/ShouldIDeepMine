using Dalamud.Configuration;

namespace ShouldIDeepMine;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public int RequestSpacingMs { get; set; } = 2200;
    public int RequestTimeoutMs { get; set; } = 12000;
    public int MaxRetries { get; set; } = 3;
    public int CategoryItemLimit { get; set; } = 500;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
