using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NovaClient.Models;

public sealed class Profile : INotifyPropertyChanged
{
    private string _name = "New instance";
    private string _minecraftVersion = "1.21.1";
    private string _loader = "Fabric";
    private int _memoryMb = 4096;
    private string _accent = "#26D96D";
    private string _icon = "⛏";
    private string _description = "Custom Minecraft instance";
    private string? _serverAddress;
    private bool _favorite;
    private bool _autoUpdateMods = true;
    private bool _showSnapshots;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public string MinecraftVersion
    {
        get => _minecraftVersion;
        set => Set(ref _minecraftVersion, value);
    }

    public string Loader
    {
        get => _loader;
        set => Set(ref _loader, value);
    }

    public int MemoryMb
    {
        get => _memoryMb;
        set => Set(ref _memoryMb, value);
    }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastPlayedAt { get; set; }
    public long TotalPlaySeconds { get; set; }

    public string Accent
    {
        get => _accent;
        set => Set(ref _accent, value);
    }

    public string Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }

    public string Description
    {
        get => _description;
        set => Set(ref _description, value);
    }

    public string? ServerAddress
    {
        get => _serverAddress;
        set => Set(ref _serverAddress, value);
    }

    public bool Favorite
    {
        get => _favorite;
        set => Set(ref _favorite, value);
    }

    public bool AutoUpdateMods
    {
        get => _autoUpdateMods;
        set => Set(ref _autoUpdateMods, value);
    }

    public bool ShowSnapshots
    {
        get => _showSnapshots;
        set => Set(ref _showSnapshots, value);
    }

    public string? JavaPath { get; set; }
    public string? CustomGameDirectory { get; set; }
    public string AdditionalJvmArguments { get; set; } = string.Empty;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public bool Fullscreen { get; set; }

    public string LoaderDisplay => string.Equals(Loader, "Vanilla", StringComparison.OrdinalIgnoreCase)
        ? MinecraftVersion
        : $"{Loader} {MinecraftVersion}";

    public string MemoryDisplay => $"{MemoryMb / 1024d:0.#} GB RAM";

    public string LastPlayedDisplay
    {
        get
        {
            if (LastPlayedAt is null) return "Never played";
            var delta = DateTime.UtcNow - LastPlayedAt.Value;
            if (delta.TotalMinutes < 1) return "Just now";
            if (delta.TotalHours < 1) return $"{Math.Max(1, (int)delta.TotalMinutes)} min ago";
            if (delta.TotalDays < 1) return $"{Math.Max(1, (int)delta.TotalHours)} h ago";
            if (delta.TotalDays < 7) return $"{Math.Max(1, (int)delta.TotalDays)} d ago";
            return LastPlayedAt.Value.ToLocalTime().ToString("dd MMM yyyy");
        }
    }

    public string PlaytimeDisplay
    {
        get
        {
            var span = TimeSpan.FromSeconds(TotalPlaySeconds);
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
            if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m";
            return "0m";
        }
    }

    public Profile Clone()
    {
        return new Profile
        {
            Id = Guid.NewGuid(),
            Name = Name + " Copy",
            MinecraftVersion = MinecraftVersion,
            Loader = Loader,
            MemoryMb = MemoryMb,
            CreatedAt = DateTime.UtcNow,
            Accent = Accent,
            Icon = Icon,
            Description = Description,
            ServerAddress = ServerAddress,
            Favorite = false,
            AutoUpdateMods = AutoUpdateMods,
            ShowSnapshots = ShowSnapshots,
            JavaPath = JavaPath,
            CustomGameDirectory = null,
            AdditionalJvmArguments = AdditionalJvmArguments,
            WindowWidth = WindowWidth,
            WindowHeight = WindowHeight,
            Fullscreen = Fullscreen
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public override string ToString() => $"{Name} • {MinecraftVersion} • {Loader}";
}
