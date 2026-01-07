using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NativeBar.WinUI.Core.Services;

namespace NativeBar.WinUI.Core.Providers.Copilot;

/// <summary>
/// GitHub OAuth Device Flow for Copilot authentication.
/// Uses the VS Code client ID to get a token that works with Copilot APIs.
/// 
/// Flow:
/// 1. Request device code from GitHub
/// 2. User visits URL and enters code
/// 3. Poll for access token
/// 4. Use token with Copilot internal API
/// </summary>
public class CopilotDeviceFlow
{
    // VS Code's registered OAuth client ID (public, not secret)
    private const string ClientId = "Iv1.b507a08c87ecfe98";
    private const string Scopes = "read:user";

    private readonly HttpClient _httpClient;

    public CopilotDeviceFlow()
    {
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Step 1: Request a device code from GitHub
    /// </summary>
    public async Task<DeviceCodeResponse> RequestDeviceCodeAsync()
    {
        var url = "https://github.com/login/device/code";

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "client_id", ClientId },
            { "scope", Scopes }
        });

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        DebugLogger.Log("CopilotDeviceFlow", $"Device code response: {json}");

        var result = JsonSerializer.Deserialize<DeviceCodeResponse>(json);
        if (result == null)
            throw new Exception("Failed to parse device code response");

        return result;
    }

    /// <summary>
    /// Step 2: Poll for access token after user authorizes
    /// </summary>
    public async Task<string> PollForTokenAsync(string deviceCode, int intervalSeconds, CancellationToken cancellationToken)
    {
        var url = "https://github.com/login/oauth/access_token";

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(intervalSeconds * 1000, cancellationToken);

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", ClientId },
                { "device_code", deviceCode },
                { "grant_type", "urn:ietf:params:oauth:grant-type:device_code" }
            });

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            DebugLogger.Log("CopilotDeviceFlow", $"Token poll response: {json}");

            // Check for error responses
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorProp))
            {
                var error = errorProp.GetString();

                if (error == "authorization_pending")
                {
                    // User hasn't authorized yet, keep polling
                    continue;
                }
                else if (error == "slow_down")
                {
                    // GitHub wants us to slow down
                    intervalSeconds += 5;
                    continue;
                }
                else if (error == "expired_token")
                {
                    throw new TimeoutException("Device code expired. Please try again.");
                }
                else if (error == "access_denied")
                {
                    throw new UnauthorizedAccessException("User denied access.");
                }
                else
                {
                    throw new Exception($"OAuth error: {error}");
                }
            }

            // Success - we got a token
            if (root.TryGetProperty("access_token", out var tokenProp))
            {
                var token = tokenProp.GetString();
                if (!string.IsNullOrEmpty(token))
                {
                    DebugLogger.Log("CopilotDeviceFlow", "Successfully obtained access token");
                    return token;
                }
            }

            throw new Exception("Unexpected response from GitHub OAuth");
        }

        throw new OperationCanceledException();
    }
}

/// <summary>
/// Response from GitHub's device code endpoint
/// </summary>
public class DeviceCodeResponse
{
    [JsonPropertyName("device_code")]
    public string DeviceCode { get; set; } = "";

    [JsonPropertyName("user_code")]
    public string UserCode { get; set; } = "";

    [JsonPropertyName("verification_uri")]
    public string VerificationUri { get; set; } = "";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; } = 5;
}
