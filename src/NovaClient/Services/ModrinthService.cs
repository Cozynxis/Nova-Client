using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using NovaClient.Models;

namespace NovaClient.Services;

public sealed class ModrinthService
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public ModrinthService()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NovaClient", "0.2.0"));
    }

    public async Task<ModrinthSearchResponse> SearchAsync(
        string query,
        string minecraftVersion,
        string loader,
        string projectType = "mod",
        string sort = "relevance",
        int offset = 0,
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        var facets = BuildFacets(minecraftVersion, loader, projectType);
        var parameters = new Dictionary<string, string>
        {
            ["query"] = query ?? string.Empty,
            ["limit"] = Math.Clamp(limit, 1, 100).ToString(),
            ["offset"] = Math.Max(0, offset).ToString(),
            ["index"] = NormalizeSort(sort),
            ["facets"] = facets
        };

        var queryString = string.Join("&", parameters.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        using var response = await _http.GetAsync(
            $"https://api.modrinth.com/v2/search?{queryString}",
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Modrinth search failed ({(int)response.StatusCode}).");

        return JsonSerializer.Deserialize<ModrinthSearchResponse>(body, _json) ?? new ModrinthSearchResponse();
    }

    public async Task<List<ModrinthVersion>> GetCompatibleVersionsAsync(
        string projectId,
        string gameVersion,
        string loader,
        string projectType = "mod",
        CancellationToken cancellationToken = default)
    {
        var versionJson = JsonSerializer.Serialize(new[] { gameVersion });
        var url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version?game_versions={Uri.EscapeDataString(versionJson)}";

        if (projectType.Equals("mod", StringComparison.OrdinalIgnoreCase))
        {
            var loaderJson = JsonSerializer.Serialize(new[] { loader.ToLowerInvariant() });
            url += $"&loaders={Uri.EscapeDataString(loaderJson)}";
        }

        using var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return [];

        return await response.Content.ReadFromJsonAsync<List<ModrinthVersion>>(_json, cancellationToken) ?? [];
    }

    public async Task<ModrinthVersion?> FindCompatibleVersionAsync(
        string projectId,
        string gameVersion,
        string loader,
        string projectType = "mod",
        CancellationToken cancellationToken = default)
    {
        var versions = await GetCompatibleVersionsAsync(
            projectId,
            gameVersion,
            loader,
            projectType,
            cancellationToken);

        return versions
            .OrderByDescending(version => version.VersionType.Equals("release", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(version => version.DatePublished)
            .FirstOrDefault();
    }

    public async Task<InstallResult> InstallAsync(
        ModrinthProject project,
        Profile profile,
        ProfileService profiles,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (project.ProjectType.Equals("modpack", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Modpack import is being built separately. Mods, shaders and resource packs already install directly.");

        if (project.ProjectType.Equals("mod", StringComparison.OrdinalIgnoreCase) &&
            profile.Loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This is a mod. Choose Fabric, Forge, NeoForge or Quilt for this instance first.");
        }

        var version = await FindCompatibleVersionAsync(
            project.ProjectId,
            profile.MinecraftVersion,
            profile.Loader,
            project.ProjectType,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"No compatible {project.ProjectType} version was found for Minecraft {profile.MinecraftVersion} / {profile.Loader}.");

        var result = new InstallResult();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installed = await LoadInstalledAsync(profile, profiles);

        var main = await InstallVersionAsync(
            project.ProjectId,
            project.Title,
            project.Slug,
            project.IconUrl,
            project.ProjectType,
            version,
            profile,
            profiles,
            installed,
            isDependency: false,
            visited,
            result,
            progress,
            0,
            0.65,
            cancellationToken);

        result.MainItem = main;
        await SaveInstalledAsync(profile, profiles, installed);
        progress?.Report(1);
        return result;
    }

    public async Task<List<InstalledContentItem>> LoadInstalledAsync(Profile profile, ProfileService profiles)
    {
        var path = GetInstalledDatabasePath(profile, profiles);
        if (!File.Exists(path)) return [];

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<List<InstalledContentItem>>(json, _json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task RemoveInstalledAsync(
        InstalledContentItem item,
        Profile profile,
        ProfileService profiles)
    {
        var installed = await LoadInstalledAsync(profile, profiles);
        var target = ResolveContentPath(item.ProjectType, profile, profiles, item.FileName);
        if (File.Exists(target)) File.Delete(target);
        if (File.Exists(target + ".disabled")) File.Delete(target + ".disabled");
        installed.RemoveAll(x => x.ProjectId == item.ProjectId && x.FileName == item.FileName);
        await SaveInstalledAsync(profile, profiles, installed);
    }

    public async Task SetEnabledAsync(
        InstalledContentItem item,
        Profile profile,
        ProfileService profiles,
        bool enabled)
    {
        if (!item.ProjectType.Equals("mod", StringComparison.OrdinalIgnoreCase))
        {
            item.Enabled = enabled;
            var content = await LoadInstalledAsync(profile, profiles);
            var current = content.FirstOrDefault(x => x.ProjectId == item.ProjectId && x.FileName == item.FileName);
            if (current is not null) current.Enabled = enabled;
            await SaveInstalledAsync(profile, profiles, content);
            return;
        }

        var normal = Path.Combine(profiles.GetModsPath(profile), item.FileName);
        var disabled = normal + ".disabled";

        if (enabled && File.Exists(disabled))
            File.Move(disabled, normal, overwrite: true);
        else if (!enabled && File.Exists(normal))
            File.Move(normal, disabled, overwrite: true);

        item.Enabled = enabled;
        var installed = await LoadInstalledAsync(profile, profiles);
        var record = installed.FirstOrDefault(x => x.ProjectId == item.ProjectId && x.FileName == item.FileName);
        if (record is not null) record.Enabled = enabled;
        await SaveInstalledAsync(profile, profiles, installed);
    }

    private async Task<InstalledContentItem> InstallVersionAsync(
        string projectId,
        string title,
        string slug,
        string? iconUrl,
        string projectType,
        ModrinthVersion version,
        Profile profile,
        ProfileService profiles,
        List<InstalledContentItem> installed,
        bool isDependency,
        HashSet<string> visited,
        InstallResult result,
        IProgress<double>? progress,
        double progressStart,
        double progressSpan,
        CancellationToken cancellationToken)
    {
        var visitKey = string.IsNullOrWhiteSpace(version.Id) ? projectId : version.Id;
        if (!visited.Add(visitKey))
        {
            return installed.FirstOrDefault(item => item.ProjectId == projectId)
                   ?? new InstalledContentItem { ProjectId = projectId, Title = title, ProjectType = projectType };
        }

        var requiredDependencies = version.Dependencies
            .Where(dependency => dependency.Type.Equals("required", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var dependencySpan = requiredDependencies.Count == 0 ? 0 : progressSpan * 0.35;
        var mainSpan = progressSpan - dependencySpan;
        var dependencyIndex = 0;

        foreach (var dependency in requiredDependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ModrinthVersion? dependencyVersion = null;
            var dependencyProjectId = dependency.ProjectId ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(dependency.VersionId))
            {
                dependencyVersion = await GetVersionByIdAsync(dependency.VersionId!, cancellationToken);
                if (string.IsNullOrWhiteSpace(dependencyProjectId))
                    dependencyProjectId = dependencyVersion?.ProjectId ?? string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(dependencyProjectId))
            {
                dependencyVersion = await FindCompatibleVersionAsync(
                    dependencyProjectId,
                    profile.MinecraftVersion,
                    profile.Loader,
                    "mod",
                    cancellationToken);
            }

            if (dependencyVersion is null || string.IsNullOrWhiteSpace(dependencyProjectId))
                continue;

            var segmentStart = progressStart + mainSpan +
                               dependencySpan * dependencyIndex / Math.Max(1, requiredDependencies.Count);
            var segmentSpan = dependencySpan / Math.Max(1, requiredDependencies.Count);

            var dep = await InstallVersionAsync(
                dependencyProjectId,
                dependencyProjectId,
                dependencyProjectId,
                null,
                "mod",
                dependencyVersion,
                profile,
                profiles,
                installed,
                true,
                visited,
                result,
                progress,
                segmentStart,
                segmentSpan,
                cancellationToken);

            if (!result.Dependencies.Any(existing => existing.ProjectId == dep.ProjectId))
                result.Dependencies.Add(dep);
            dependencyIndex++;
        }

        var file = version.Files.FirstOrDefault(candidate => candidate.Primary)
                   ?? version.Files.FirstOrDefault()
                   ?? throw new InvalidOperationException($"{title} has no downloadable file in this version.");

        var destination = ResolveContentPath(projectType, profile, profiles, file.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        await DownloadFileAsync(
            file,
            destination,
            value => progress?.Report(progressStart + value * mainSpan),
            cancellationToken);

        var item = new InstalledContentItem
        {
            ProjectId = projectId,
            ProjectType = projectType,
            Title = title,
            Slug = slug,
            VersionId = version.Id,
            VersionNumber = version.VersionNumber,
            FileName = file.FileName,
            IconUrl = iconUrl,
            InstalledAtUtc = DateTime.UtcNow,
            Enabled = true,
            IsDependency = isDependency
        };

        installed.RemoveAll(existing =>
            existing.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase) &&
            existing.ProjectType.Equals(projectType, StringComparison.OrdinalIgnoreCase));
        installed.Add(item);
        result.DownloadedFiles.Add(destination);
        return item;
    }

    private async Task DownloadFileAsync(
        ModrinthFile file,
        string destination,
        Action<double>? progress,
        CancellationToken cancellationToken)
    {
        var temporary = destination + ".nova-download";
        if (File.Exists(temporary)) File.Delete(temporary);

        try
        {
            using var response = await _http.GetAsync(
                file.Url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? file.Size;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(temporary);
            var buffer = new byte[128 * 1024];
            long downloaded = 0;

            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                if (totalBytes > 0)
                    progress?.Invoke(Math.Clamp(downloaded / (double)totalBytes, 0, 1));
            }

            await output.FlushAsync(cancellationToken);
            output.Close();

            await VerifyHashesAsync(temporary, file, cancellationToken);
            File.Move(temporary, destination, overwrite: true);
            progress?.Invoke(1);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private static async Task VerifyHashesAsync(
        string filePath,
        ModrinthFile file,
        CancellationToken cancellationToken)
    {
        if (file.Hashes.Count == 0) return;

        string? expected = null;
        HashAlgorithm? algorithm = null;

        if (file.Hashes.TryGetValue("sha512", out var sha512))
        {
            expected = sha512;
            algorithm = SHA512.Create();
        }
        else if (file.Hashes.TryGetValue("sha1", out var sha1))
        {
            expected = sha1;
            algorithm = SHA1.Create();
        }

        if (expected is null || algorithm is null) return;
        using (algorithm)
        await using (var stream = File.OpenRead(filePath))
        {
            var hash = await algorithm.ComputeHashAsync(stream, cancellationToken);
            var actual = Convert.ToHexString(hash).ToLowerInvariant();
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Downloaded file failed Modrinth hash verification.");
        }
    }

    private async Task<ModrinthVersion?> GetVersionByIdAsync(string versionId, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(
            $"https://api.modrinth.com/v2/version/{Uri.EscapeDataString(versionId)}",
            cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ModrinthVersion>(_json, cancellationToken);
    }

    private async Task SaveInstalledAsync(
        Profile profile,
        ProfileService profiles,
        List<InstalledContentItem> items)
    {
        var path = GetInstalledDatabasePath(profile, profiles);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(items, _json));
    }

    private static string GetInstalledDatabasePath(Profile profile, ProfileService profiles)
        => Path.Combine(profiles.GetInstancePath(profile), "installed-content.json");

    private static string ResolveContentPath(
        string projectType,
        Profile profile,
        ProfileService profiles,
        string fileName)
    {
        var root = projectType.ToLowerInvariant() switch
        {
            "shader" => profiles.GetShaderPacksPath(profile),
            "resourcepack" => profiles.GetResourcePacksPath(profile),
            _ => profiles.GetModsPath(profile)
        };
        return Path.Combine(root, fileName);
    }

    private static string BuildFacets(string version, string loader, string projectType)
    {
        var facets = new List<List<string>>();
        if (!string.IsNullOrWhiteSpace(version))
            facets.Add([$"versions:{version}"]);
        if (!string.IsNullOrWhiteSpace(projectType))
            facets.Add([$"project_type:{projectType}"]);
        if (projectType.Equals("mod", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(loader) &&
            !loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
        {
            facets.Add([$"categories:{loader.ToLowerInvariant()}"]);
        }
        return JsonSerializer.Serialize(facets);
    }

    private static string NormalizeSort(string sort)
    {
        return sort.ToLowerInvariant() switch
        {
            "downloads" => "downloads",
            "followers" => "follows",
            "newest" => "newest",
            "updated" => "updated",
            _ => "relevance"
        };
    }
}
