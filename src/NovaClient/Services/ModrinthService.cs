using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NovaClient.Models;
namespace NovaClient.Services;
public sealed class ModrinthService
{
    private readonly HttpClient _http = new();
    private readonly JsonSerializerOptions _json = new(){PropertyNameCaseInsensitive=true};
    public ModrinthService(){ _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NovaClient","0.1.0")); }
    public async Task<List<ModrinthProject>> SearchAsync(string query, string minecraftVersion, string loader)
    {
        var facets = $"[[\"versions:{minecraftVersion}\"],[\"categories:{loader.ToLowerInvariant()}\"],[\"project_type:mod\"]]";
        var url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&limit=24&facets={Uri.EscapeDataString(facets)}";
        var response = await _http.GetFromJsonAsync<ModrinthSearchResponse>(url,_json);
        return response?.Hits ?? [];
    }
    public async Task<ModrinthVersion?> FindCompatibleVersionAsync(string projectId,string gameVersion,string loader)
    {
        var url=$"https://api.modrinth.com/v2/project/{projectId}/version?game_versions={Uri.EscapeDataString($"[\"{gameVersion}\"]")}&loaders={Uri.EscapeDataString($"[\"{loader.ToLowerInvariant()}\"]")}";
        var versions=await _http.GetFromJsonAsync<List<ModrinthVersion>>(url,_json);
        return versions?.FirstOrDefault();
    }
    public async Task<string> InstallAsync(ModrinthProject project, Profile profile, ProfileService profiles, IProgress<double>? progress=null)
    {
        if(profile.Loader.Equals("Vanilla",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose Fabric, Forge, NeoForge or Quilt for mod installation.");
        var version=await FindCompatibleVersionAsync(project.ProjectId,profile.MinecraftVersion,profile.Loader) ?? throw new InvalidOperationException("No compatible version found.");
        var file=version.Files.FirstOrDefault(f=>f.Primary) ?? version.Files.FirstOrDefault() ?? throw new InvalidOperationException("This version has no downloadable file.");
        var target=Path.Combine(profiles.GetModsPath(profile),file.FileName);
        using var res=await _http.GetAsync(file.Url,HttpCompletionOption.ResponseHeadersRead); res.EnsureSuccessStatusCode();
        var len=res.Content.Headers.ContentLength; await using var input=await res.Content.ReadAsStreamAsync(); await using var output=File.Create(target);
        var buffer=new byte[81920]; long total=0; int read; while((read=await input.ReadAsync(buffer))>0){ await output.WriteAsync(buffer.AsMemory(0,read)); total+=read; if(len>0) progress?.Report((double)total/len.Value); }
        return target;
    }
}
