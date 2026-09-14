using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
namespace NovaClient.Services;
public sealed class MicrosoftAuthService
{
    private readonly HttpClient _http = new();
    public const string ClientIdEnvironmentVariable = "NOVA_MICROSOFT_CLIENT_ID";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ClientIdEnvironmentVariable));
    public async Task<DeviceCodeSession> BeginDeviceCodeAsync()
    {
        var clientId=Environment.GetEnvironmentVariable(ClientIdEnvironmentVariable);
        if(string.IsNullOrWhiteSpace(clientId)) throw new InvalidOperationException($"Microsoft OAuth is not configured yet. Set {ClientIdEnvironmentVariable} to your registered public-client Application ID.");
        var form=new FormUrlEncodedContent(new Dictionary<string,string>{{"client_id",clientId},{"scope","XboxLive.signin offline_access"}});
        using var res=await _http.PostAsync("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode",form); res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<DeviceCodeSession>()) ?? throw new InvalidOperationException("Microsoft did not return a device code.");
    }
    public void OpenVerificationPage(DeviceCodeSession session){ if(Uri.TryCreate(session.VerificationUri,out var uri)) Process.Start(new ProcessStartInfo(uri.ToString()){UseShellExecute=true}); }
}
public sealed class DeviceCodeSession
{
    [JsonPropertyName("device_code")] public string DeviceCode { get; set; }="";
    [JsonPropertyName("user_code")] public string UserCode { get; set; }="";
    [JsonPropertyName("verification_uri")] public string VerificationUri { get; set; }="https://microsoft.com/devicelogin";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; }="";
}
