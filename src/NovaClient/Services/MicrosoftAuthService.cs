using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NovaClient.Models;

namespace NovaClient.Services;

public sealed class MicrosoftAuthService
{
    private const string CredentialTarget = "NovaClient/MicrosoftRefreshToken";
    private const string DeviceCodeEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string XboxAuthEndpoint = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsEndpoint = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string MinecraftLoginEndpoint = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string MinecraftProfileEndpoint = "https://api.minecraftservices.com/minecraft/profile";
    private const string MinecraftEntitlementsEndpoint = "https://api.minecraftservices.com/entitlements/mcstore";

    private readonly HttpClient _http;
    private readonly WindowsCredentialStore _credentials;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MicrosoftAuthService()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NovaClient/0.2");
        _credentials = new WindowsCredentialStore();
    }

    public bool HasStoredLogin => !string.IsNullOrWhiteSpace(_credentials.Read(CredentialTarget));

    public async Task<DeviceCodeSession> BeginDeviceCodeAsync(string clientId, CancellationToken cancellationToken = default)
    {
        ValidateClientId(clientId);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = "XboxLive.signin offline_access"
        });

        using var response = await _http.PostAsync(DeviceCodeEndpoint, form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(BuildOAuthError("Microsoft device login could not start", body));

        return JsonSerializer.Deserialize<DeviceCodeSession>(body, _json)
               ?? throw new InvalidOperationException("Microsoft did not return a device-code session.");
    }

    public void OpenVerificationPage(DeviceCodeSession session)
    {
        if (!Uri.TryCreate(session.VerificationUri, UriKind.Absolute, out var uri) || uri is null)
            return;

        Process.Start(new ProcessStartInfo(uri.ToString())
        {
            UseShellExecute = true
        });
    }

    public async Task<MicrosoftTokenSet> WaitForMicrosoftTokenAsync(
        string clientId,
        DeviceCodeSession session,
        IProgress<AccountLoginProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateClientId(clientId);
        var started = DateTime.UtcNow;
        var delaySeconds = Math.Max(1, session.Interval);

        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(Math.Max(60, session.ExpiresIn)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AccountLoginProgress
            {
                Stage = "Waiting for Microsoft",
                Detail = "Finish signing in in your browser…",
                Percent = 15
            });

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = session.DeviceCode
            });

            using var response = await _http.PostAsync(TokenEndpoint, form, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
                return ParseMicrosoftToken(body);

            using var document = JsonDocument.Parse(body);
            var error = document.RootElement.TryGetProperty("error", out var errorElement)
                ? errorElement.GetString()
                : null;

            switch (error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    delaySeconds += 2;
                    continue;
                case "authorization_declined":
                    throw new OperationCanceledException("Microsoft sign-in was cancelled.");
                case "expired_token":
                    throw new InvalidOperationException("The Microsoft sign-in code expired. Start sign-in again.");
                default:
                    throw new InvalidOperationException(BuildOAuthError("Microsoft sign-in failed", body));
            }
        }

        throw new TimeoutException("Microsoft sign-in timed out. Start the sign-in process again.");
    }

    public async Task<MinecraftAccount> CompleteMinecraftLoginAsync(
        MicrosoftTokenSet microsoft,
        IProgress<AccountLoginProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new AccountLoginProgress
        {
            Stage = "Xbox Live",
            Detail = "Authenticating your Microsoft account with Xbox Live…",
            Percent = 35
        });

        var xbox = await AuthenticateXboxAsync(microsoft.AccessToken, cancellationToken);

        progress?.Report(new AccountLoginProgress
        {
            Stage = "Xbox Security",
            Detail = "Requesting an XSTS token…",
            Percent = 50
        });

        var xsts = await AuthenticateXstsAsync(xbox.Token, cancellationToken);

        progress?.Report(new AccountLoginProgress
        {
            Stage = "Minecraft Services",
            Detail = "Creating your Minecraft session…",
            Percent = 67
        });

        var minecraftToken = await AuthenticateMinecraftAsync(xsts.UserHash, xsts.Token, cancellationToken);

        progress?.Report(new AccountLoginProgress
        {
            Stage = "Checking ownership",
            Detail = "Checking Minecraft: Java Edition access…",
            Percent = 78
        });

        var ownsJava = await CheckJavaOwnershipAsync(minecraftToken.AccessToken, cancellationToken);

        progress?.Report(new AccountLoginProgress
        {
            Stage = "Loading profile",
            Detail = "Getting your Minecraft username and skin…",
            Percent = 90
        });

        var profile = await GetMinecraftProfileAsync(minecraftToken.AccessToken, cancellationToken);
        profile.OwnsJavaEdition = ownsJava;
        profile.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, minecraftToken.ExpiresIn - 60));

        if (!string.IsNullOrWhiteSpace(microsoft.RefreshToken))
            _credentials.Save(CredentialTarget, profile.Name, microsoft.RefreshToken);

        progress?.Report(new AccountLoginProgress
        {
            Stage = "Connected",
            Detail = $"Signed in as {profile.Name}",
            Percent = 100
        });

        return profile;
    }

    public async Task<MinecraftAccount> SignInAsync(
        string clientId,
        Action<DeviceCodeSession>? deviceCodeReady = null,
        IProgress<AccountLoginProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var device = await BeginDeviceCodeAsync(clientId, cancellationToken);
        deviceCodeReady?.Invoke(device);
        OpenVerificationPage(device);

        var microsoft = await WaitForMicrosoftTokenAsync(
            clientId,
            device,
            progress,
            cancellationToken);

        return await CompleteMinecraftLoginAsync(microsoft, progress, cancellationToken);
    }

    public async Task<MinecraftAccount?> TryRestoreSessionAsync(
        string clientId,
        IProgress<AccountLoginProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return null;
        var refreshToken = _credentials.Read(CredentialTarget);
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;

        try
        {
            progress?.Report(new AccountLoginProgress
            {
                Stage = "Restoring account",
                Detail = "Refreshing your Microsoft session…",
                Percent = 10
            });

            var microsoft = await RefreshMicrosoftTokenAsync(clientId, refreshToken, cancellationToken);
            return await CompleteMinecraftLoginAsync(microsoft, progress, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public void SignOut()
    {
        _credentials.Delete(CredentialTarget);
    }

    private async Task<MicrosoftTokenSet> RefreshMicrosoftTokenAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        ValidateClientId(clientId);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = "XboxLive.signin offline_access",
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });

        using var response = await _http.PostAsync(TokenEndpoint, form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(BuildOAuthError("Microsoft session refresh failed", body));

        return ParseMicrosoftToken(body);
    }

    private async Task<XboxToken> AuthenticateXboxAsync(string microsoftAccessToken, CancellationToken cancellationToken)
    {
        var payload = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = "d=" + microsoftAccessToken
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };

        using var response = await _http.PostAsJsonAsync(XboxAuthEndpoint, payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Xbox Live authentication failed ({(int)response.StatusCode}).");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var token = root.GetProperty("Token").GetString()
                    ?? throw new InvalidOperationException("Xbox Live returned no token.");
        var userHash = root
            .GetProperty("DisplayClaims")
            .GetProperty("xui")[0]
            .GetProperty("uhs")
            .GetString()
            ?? throw new InvalidOperationException("Xbox Live returned no user hash.");

        return new XboxToken(token, userHash);
    }

    private async Task<XboxToken> AuthenticateXstsAsync(string xboxToken, CancellationToken cancellationToken)
    {
        var payload = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xboxToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };

        using var response = await _http.PostAsJsonAsync(XstsEndpoint, payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var friendly = response.StatusCode == HttpStatusCode.Unauthorized
                ? "Xbox/XSTS rejected this account. Check Xbox privacy/family settings and that the Microsoft account has an Xbox profile."
                : $"XSTS authentication failed ({(int)response.StatusCode}).";
            throw new InvalidOperationException(friendly);
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var token = root.GetProperty("Token").GetString()
                    ?? throw new InvalidOperationException("XSTS returned no token.");
        var userHash = root
            .GetProperty("DisplayClaims")
            .GetProperty("xui")[0]
            .GetProperty("uhs")
            .GetString()
            ?? throw new InvalidOperationException("XSTS returned no user hash.");

        return new XboxToken(token, userHash);
    }

    private async Task<MinecraftToken> AuthenticateMinecraftAsync(
        string userHash,
        string xstsToken,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            identityToken = $"XBL3.0 x={userHash};{xstsToken}"
        };

        using var response = await _http.PostAsJsonAsync(MinecraftLoginEndpoint, payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Minecraft Services login failed ({(int)response.StatusCode}).");

        using var document = JsonDocument.Parse(body);
        var token = document.RootElement.GetProperty("access_token").GetString()
                    ?? throw new InvalidOperationException("Minecraft Services returned no access token.");
        var expires = document.RootElement.TryGetProperty("expires_in", out var expiry)
            ? expiry.GetInt32()
            : 3600;

        return new MinecraftToken(token, expires);
    }

    private async Task<bool> CheckJavaOwnershipAsync(string minecraftToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MinecraftEntitlementsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return false;

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return false;

        return items.GetArrayLength() > 0;
    }

    private async Task<MinecraftAccount> GetMinecraftProfileAsync(string minecraftToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MinecraftProfileEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("This Microsoft account does not have a Minecraft: Java Edition profile.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Minecraft profile request failed ({(int)response.StatusCode}).");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var id = root.GetProperty("id").GetString() ?? string.Empty;
        var name = root.GetProperty("name").GetString() ?? "Minecraft Player";

        string? skin = null;
        if (root.TryGetProperty("skins", out var skins) && skins.ValueKind == JsonValueKind.Array && skins.GetArrayLength() > 0)
            skin = skins[0].TryGetProperty("url", out var skinUrl) ? skinUrl.GetString() : null;

        string? cape = null;
        if (root.TryGetProperty("capes", out var capes) && capes.ValueKind == JsonValueKind.Array && capes.GetArrayLength() > 0)
            cape = capes[0].TryGetProperty("url", out var capeUrl) ? capeUrl.GetString() : null;

        return new MinecraftAccount
        {
            Id = id,
            Name = name,
            SkinUrl = skin,
            CapeUrl = cape,
            AccessToken = minecraftToken
        };
    }

    private MicrosoftTokenSet ParseMicrosoftToken(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new MicrosoftTokenSet
        {
            AccessToken = root.GetProperty("access_token").GetString() ?? string.Empty,
            RefreshToken = root.TryGetProperty("refresh_token", out var refresh)
                ? refresh.GetString() ?? string.Empty
                : string.Empty,
            ExpiresIn = root.TryGetProperty("expires_in", out var expiry)
                ? expiry.GetInt32()
                : 3600
        };
    }

    private static void ValidateClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException(
                "Microsoft sign-in is not configured yet. Open Settings → Account and enter Nova Client's Microsoft Entra Application (client) ID.");

        if (!Guid.TryParse(clientId, out _))
            throw new InvalidOperationException("The Microsoft Application (client) ID is not a valid GUID.");
    }

    private static string BuildOAuthError(string title, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var description = root.TryGetProperty("error_description", out var value)
                ? value.GetString()
                : null;
            return string.IsNullOrWhiteSpace(description) ? title : $"{title}: {description}";
        }
        catch
        {
            return title;
        }
    }

    private sealed record XboxToken(string Token, string UserHash);
    private sealed record MinecraftToken(string AccessToken, int ExpiresIn);
}

public sealed class DeviceCodeSession
{
    [JsonPropertyName("device_code")]
    public string DeviceCode { get; set; } = string.Empty;

    [JsonPropertyName("user_code")]
    public string UserCode { get; set; } = string.Empty;

    [JsonPropertyName("verification_uri")]
    public string VerificationUri { get; set; } = "https://microsoft.com/devicelogin";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; } = 5;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
