using System.Diagnostics;
using System.Text.Json;
using NovaClient.Models;

namespace NovaClient.Services;

public sealed class ProfileService
{
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NovaClient");

    private string DataFile => Path.Combine(_root, "profiles.json");
    public string InstancesRoot => Path.Combine(_root, "instances");

    public ProfileService()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(InstancesRoot);
    }

    public async Task<List<Profile>> LoadAsync()
    {
        if (!File.Exists(DataFile))
        {
            var defaults = CreateDefaultProfiles();
            foreach (var profile in defaults)
                EnsureInstanceStructure(profile);
            await SaveAsync(defaults);
            return defaults;
        }

        try
        {
            var json = await File.ReadAllTextAsync(DataFile);
            var profiles = JsonSerializer.Deserialize<List<Profile>>(json, _json) ?? [];
            foreach (var profile in profiles)
                EnsureInstanceStructure(profile);
            return profiles;
        }
        catch
        {
            return [];
        }
    }

    public Task SaveAsync(IEnumerable<Profile> profiles)
    {
        Directory.CreateDirectory(_root);
        return File.WriteAllTextAsync(DataFile, JsonSerializer.Serialize(profiles, _json));
    }

    public Profile Create(string name, string version, string loader, int memoryMb)
    {
        var profile = new Profile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "New instance" : name.Trim(),
            MinecraftVersion = string.IsNullOrWhiteSpace(version) ? "1.21.1" : version.Trim(),
            Loader = string.IsNullOrWhiteSpace(loader) ? "Vanilla" : loader.Trim(),
            MemoryMb = Math.Clamp(memoryMb, 2048, 32768),
            CreatedAt = DateTime.UtcNow,
            Icon = loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase) ? "🧵" : "⛏",
            Description = "Custom Minecraft instance"
        };

        EnsureInstanceStructure(profile);
        return profile;
    }

    public Profile Duplicate(Profile source)
    {
        var clone = source.Clone();
        var sourcePath = GetInstancePath(source);
        var clonePath = GetInstancePath(clone);

        CopyDirectoryIfExists(Path.Combine(sourcePath, "mods"), Path.Combine(clonePath, "mods"));
        CopyDirectoryIfExists(Path.Combine(sourcePath, "config"), Path.Combine(clonePath, "config"));
        CopyDirectoryIfExists(Path.Combine(sourcePath, "resourcepacks"), Path.Combine(clonePath, "resourcepacks"));
        CopyDirectoryIfExists(Path.Combine(sourcePath, "shaderpacks"), Path.Combine(clonePath, "shaderpacks"));
        CopyFileIfExists(Path.Combine(sourcePath, "installed-mods.json"), Path.Combine(clonePath, "installed-mods.json"));
        return clone;
    }

    public void Delete(Profile profile)
    {
        var path = GetInstancePath(profile, create: false);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    public string GetInstancePath(Profile profile, bool create = true)
    {
        var path = !string.IsNullOrWhiteSpace(profile.CustomGameDirectory)
            ? profile.CustomGameDirectory!
            : Path.Combine(InstancesRoot, profile.Id.ToString("N"));

        if (create)
            EnsureInstanceStructure(profile, path);
        return path;
    }

    public string GetModsPath(Profile profile)
    {
        var path = Path.Combine(GetInstancePath(profile), "mods");
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetConfigPath(Profile profile)
    {
        var path = Path.Combine(GetInstancePath(profile), "config");
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetResourcePacksPath(Profile profile)
    {
        var path = Path.Combine(GetInstancePath(profile), "resourcepacks");
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetShaderPacksPath(Profile profile)
    {
        var path = Path.Combine(GetInstancePath(profile), "shaderpacks");
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetSavesPath(Profile profile)
    {
        var path = Path.Combine(GetInstancePath(profile), "saves");
        Directory.CreateDirectory(path);
        return path;
    }

    public int CountMods(Profile profile)
    {
        var path = GetModsPath(profile);
        return Directory.EnumerateFiles(path, "*.jar", SearchOption.TopDirectoryOnly).Count();
    }

    public long GetInstanceSize(Profile profile)
    {
        var path = GetInstancePath(profile);
        if (!Directory.Exists(path)) return 0;

        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(file => new FileInfo(file).Length)
                .Sum();
        }
        catch
        {
            return 0;
        }
    }

    public void OpenInstanceFolder(Profile profile)
    {
        var path = GetInstancePath(profile);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void OpenModsFolder(Profile profile)
    {
        var path = GetModsPath(profile);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void TouchPlayed(Profile profile)
    {
        profile.LastPlayedAt = DateTime.UtcNow;
    }

    private void EnsureInstanceStructure(Profile profile, string? explicitPath = null)
    {
        var path = explicitPath ?? (!string.IsNullOrWhiteSpace(profile.CustomGameDirectory)
            ? profile.CustomGameDirectory!
            : Path.Combine(InstancesRoot, profile.Id.ToString("N")));

        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, "mods"));
        Directory.CreateDirectory(Path.Combine(path, "config"));
        Directory.CreateDirectory(Path.Combine(path, "resourcepacks"));
        Directory.CreateDirectory(Path.Combine(path, "shaderpacks"));
        Directory.CreateDirectory(Path.Combine(path, "saves"));
        Directory.CreateDirectory(Path.Combine(path, "screenshots"));
        Directory.CreateDirectory(Path.Combine(path, "logs"));
    }

    private static List<Profile> CreateDefaultProfiles()
    {
        return
        [
            new Profile
            {
                Name = "Performance",
                MinecraftVersion = "1.21.1",
                Loader = "Fabric",
                MemoryMb = 6144,
                Accent = "#26D96D",
                Icon = "⚡",
                Favorite = true,
                Description = "Fast Fabric setup for performance mods"
            },
            new Profile
            {
                Name = "Vanilla",
                MinecraftVersion = "1.21.1",
                Loader = "Vanilla",
                MemoryMb = 4096,
                Accent = "#7D8A9A",
                Icon = "⛏",
                Description = "Clean vanilla Minecraft"
            }
        ];
    }

    private static void CopyDirectoryIfExists(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectoryIfExists(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void CopyFileIfExists(string source, string destination)
    {
        if (File.Exists(source))
            File.Copy(source, destination, overwrite: true);
    }
}
