using System.Diagnostics;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ProcessBuilder;
using NovaClient.Models;

namespace NovaClient.Services;

public sealed class MinecraftLauncherService
{
    private readonly HttpClient _httpClient = new();

    public async Task<Process> LaunchAsync(
        Profile profile,
        MinecraftAccount account,
        ProfileService profileService,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (account is null || !account.IsAccessTokenValid)
            throw new InvalidOperationException("Your Minecraft session is missing or expired. Sign in again first.");

        if (!account.OwnsJavaEdition)
            throw new InvalidOperationException("This Microsoft account does not appear to own Minecraft: Java Edition.");

        var instancePath = profileService.GetInstancePath(profile);
        var minecraftPath = new MinecraftPath(instancePath);
        var launcher = new MinecraftLauncher(minecraftPath);

        progress?.Report(new LauncherProgress
        {
            Stage = "Preparing instance",
            Detail = $"Minecraft {profile.MinecraftVersion} • {profile.Loader}",
            Percent = 5
        });

        var versionName = await ResolveLaunchVersionAsync(profile, minecraftPath, progress, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LauncherProgress
        {
            Stage = "Installing Minecraft files",
            Detail = "Checking libraries, assets and Java requirements…",
            Percent = 25
        });

        var launchOption = new MLaunchOption
        {
            Session = new MSession
            {
                Username = account.Name,
                UUID = account.Id,
                AccessToken = account.AccessToken
            },
            MaximumRamMb = profile.MemoryMb,
            MinimumRamMb = Math.Min(1024, profile.MemoryMb),
            FullScreen = profile.Fullscreen,
            ScreenWidth = profile.WindowWidth,
            ScreenHeight = profile.WindowHeight,
            GameLauncherName = "Nova Client",
            GameLauncherVersion = "0.2.0"
        };

        if (!string.IsNullOrWhiteSpace(profile.JavaPath))
            launchOption.JavaPath = profile.JavaPath;

        if (!string.IsNullOrWhiteSpace(profile.ServerAddress))
            ApplyServer(profile.ServerAddress!, launchOption);

        progress?.Report(new LauncherProgress
        {
            Stage = "Building launch process",
            Detail = "Downloading anything missing and preparing Java…",
            Percent = 45
        });

        var process = await launcher.InstallAndBuildProcessAsync(versionName, launchOption);
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new LauncherProgress
        {
            Stage = "Starting Minecraft",
            Detail = $"Launching {profile.Name}…",
            Percent = 88
        });

        process.EnableRaisingEvents = true;
        process.Start();

        progress?.Report(new LauncherProgress
        {
            Stage = "Running",
            Detail = $"Minecraft started as {account.Name}",
            Percent = 100
        });

        return process;
    }

    private async Task<string> ResolveLaunchVersionAsync(
        Profile profile,
        MinecraftPath minecraftPath,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        var loader = profile.Loader.Trim();
        if (loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
            return profile.MinecraftVersion;

        if (loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report(new LauncherProgress
            {
                Stage = "Installing Fabric",
                Detail = "Finding the latest compatible Fabric Loader…",
                Percent = 14
            });

            cancellationToken.ThrowIfCancellationRequested();
            var fabric = new FabricInstaller(_httpClient);
            return await fabric.Install(profile.MinecraftVersion, minecraftPath);
        }

        throw new NotSupportedException(
            $"{loader} launch support is not finished in Nova 0.2 Preview yet. Vanilla and Fabric are fully wired in this build.");
    }

    private static void ApplyServer(string value, MLaunchOption option)
    {
        var address = value.Trim();
        if (address.Length == 0) return;

        var lastColon = address.LastIndexOf(':');
        if (lastColon > 0 && lastColon < address.Length - 1 &&
            int.TryParse(address[(lastColon + 1)..], out var port))
        {
            option.ServerIp = address[..lastColon];
            option.ServerPort = port;
        }
        else
        {
            option.ServerIp = address;
        }
    }
}

public sealed class LauncherProgress
{
    public string Stage { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public int Percent { get; set; }
}
