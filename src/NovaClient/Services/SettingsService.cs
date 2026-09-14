using System.Text.Json;
using NovaClient.Models;

namespace NovaClient.Services;

public sealed class SettingsService
{
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string RootPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NovaClient");

    public string SettingsPath => Path.Combine(RootPath, "settings.json");
    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        Directory.CreateDirectory(RootPath);
    }

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Current = new AppSettings();
                await SaveAsync();
                return Current;
            }

            var json = await File.ReadAllTextAsync(SettingsPath);
            Current = JsonSerializer.Deserialize<AppSettings>(json, _json) ?? new AppSettings();
            return Current;
        }
        catch
        {
            Current = new AppSettings();
            return Current;
        }
    }

    public Task SaveAsync()
    {
        Directory.CreateDirectory(RootPath);
        return File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(Current, _json));
    }

    public async Task UpdateAsync(Action<AppSettings> update)
    {
        update(Current);
        await SaveAsync();
    }
}
