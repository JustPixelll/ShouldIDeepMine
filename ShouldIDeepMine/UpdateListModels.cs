namespace ShouldIDeepMine;

[Serializable]
public sealed class DeepMineUpdateList
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New list";
    public List<uint> ItemIds { get; set; } = new();
}
