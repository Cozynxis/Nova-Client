using System.Text.Json.Serialization;
namespace NovaClient.Models;
public sealed class ModrinthSearchResponse { [JsonPropertyName("hits")] public List<ModrinthProject> Hits { get; set; } = []; }
public sealed class ModrinthProject
{
    [JsonPropertyName("project_id")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("slug")] public string Slug { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("downloads")] public long Downloads { get; set; }
    [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
    public string DownloadText => Downloads >= 1_000_000 ? $"{Downloads / 1_000_000d:0.#}M downloads" : $"{Downloads / 1000d:0.#}K downloads";
}
public sealed class ModrinthVersion
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("version_number")] public string VersionNumber { get; set; } = "";
    [JsonPropertyName("files")] public List<ModrinthFile> Files { get; set; } = [];
    [JsonPropertyName("dependencies")] public List<ModrinthDependency> Dependencies { get; set; } = [];
}
public sealed class ModrinthFile { [JsonPropertyName("url")] public string Url { get; set; } = ""; [JsonPropertyName("filename")] public string FileName { get; set; } = ""; [JsonPropertyName("primary")] public bool Primary { get; set; } }
public sealed class ModrinthDependency { [JsonPropertyName("project_id")] public string? ProjectId { get; set; } [JsonPropertyName("dependency_type")] public string Type { get; set; } = ""; }
