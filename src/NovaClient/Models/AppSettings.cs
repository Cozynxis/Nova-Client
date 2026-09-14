namespace NovaClient.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string Accent { get; set; } = "#26D96D";
    public int DefaultMemoryMb { get; set; } = 6144;
    public int MaxConcurrentDownloads { get; set; } = 4;
    public bool MinimizeOnLaunch { get; set; } = true;
    public bool CloseOnLaunch { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public bool ShowNews { get; set; } = true;
    public bool ReducedMotion { get; set; }
    public bool UseSystemJava { get; set; } = true;
    public string? JavaPath { get; set; }
    public string MicrosoftClientId { get; set; } = string.Empty;
    public Guid? SelectedProfileId { get; set; }
    public string LastDiscoverQuery { get; set; } = "sodium";
    public string DiscoverProjectType { get; set; } = "mod";
    public string DiscoverSort { get; set; } = "relevance";
    public int WindowWidth { get; set; } = 1480;
    public int WindowHeight { get; set; } = 900;
}
