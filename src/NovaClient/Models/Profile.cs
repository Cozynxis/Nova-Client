namespace NovaClient.Models;
public sealed class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New profile";
    public string MinecraftVersion { get; set; } = "1.21.1";
    public string Loader { get; set; } = "Fabric";
    public int MemoryMb { get; set; } = 4096;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastPlayedAt { get; set; }
    public string Accent { get; set; } = "#8B5CF6";
    public override string ToString() => $"{Name}  •  {MinecraftVersion}  •  {Loader}";
}
