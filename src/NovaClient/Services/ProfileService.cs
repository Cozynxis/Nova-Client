using System.IO;
using System.Text.Json;
using NovaClient.Models;
namespace NovaClient.Services;
public sealed class ProfileService
{
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NovaClient");
    private string DataFile => Path.Combine(_root, "profiles.json");
    public string InstancesRoot => Path.Combine(_root, "instances");
    public ProfileService() { Directory.CreateDirectory(_root); Directory.CreateDirectory(InstancesRoot); }
    public async Task<List<Profile>> LoadAsync()
    {
        if (!File.Exists(DataFile)) { var defaults = new List<Profile>{ new(){Name="Performance",MinecraftVersion="1.21.1",Loader="Fabric",MemoryMb=6144}, new(){Name="Vanilla",MinecraftVersion="1.21.1",Loader="Vanilla",MemoryMb=4096} }; await SaveAsync(defaults); return defaults; }
        try { return JsonSerializer.Deserialize<List<Profile>>(await File.ReadAllTextAsync(DataFile)) ?? []; } catch { return []; }
    }
    public Task SaveAsync(IEnumerable<Profile> profiles) => File.WriteAllTextAsync(DataFile, JsonSerializer.Serialize(profiles, new JsonSerializerOptions{WriteIndented=true}));
    public string GetInstancePath(Profile p) { var path = Path.Combine(InstancesRoot, p.Id.ToString("N")); Directory.CreateDirectory(path); Directory.CreateDirectory(Path.Combine(path,"mods")); return path; }
    public string GetModsPath(Profile p) => Path.Combine(GetInstancePath(p), "mods");
}
