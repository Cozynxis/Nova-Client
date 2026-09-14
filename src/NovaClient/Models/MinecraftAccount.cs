namespace NovaClient.Models;

public sealed class MinecraftAccount
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public string? SkinUrl { get; set; }
    public string? CapeUrl { get; set; }
    public bool OwnsJavaEdition { get; set; }
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Minecraft account" : Name;
    public bool IsAccessTokenValid => !string.IsNullOrWhiteSpace(AccessToken) && ExpiresAtUtc > DateTime.UtcNow.AddMinutes(2);
}

public sealed class MicrosoftTokenSet
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
}

public sealed class AccountLoginProgress
{
    public string Stage { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public int Percent { get; set; }
}
