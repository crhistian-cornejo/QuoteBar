using NativeBar.WinUI.Core.Models;
using NativeBar.WinUI.Core.Services;

namespace NativeBar.WinUI.Core.Providers.MiniMax;

/// <summary>
/// MiniMax provider descriptor
/// MiniMax is web-only - usage is fetched from the Coding Plan remains API using a session cookie header.
/// </summary>
public class MiniMaxProviderDescriptor : ProviderDescriptor
{
    public override string Id => "minimax";
    public override string DisplayName => "MiniMax";
    public override string IconGlyph => "\uE950"; // Star icon (same as z.ai)
    public override string PrimaryColor => "#E2167E"; // Pink (gradient start)
    public override string SecondaryColor => "#FE603C"; // Orange (gradient end)
    public override string PrimaryLabel => "Prompts";
    public override string SecondaryLabel => "Window";
    public override string? DashboardUrl => "https://platform.minimax.io/user-center/payment/coding-plan?cycle_type=3";

    public override bool SupportsOAuth => false;
    public override bool SupportsCLI => false;
    public override bool SupportsWebScraping => true;

    protected override void InitializeStrategies()
    {
        AddStrategy(new MiniMaxCookieStrategy());
    }
}

/// <summary>
/// Cookie-based strategy for MiniMax
/// Uses manual cookie header or environment variable
/// </summary>
public class MiniMaxCookieStrategy : IProviderFetchStrategy
{
    public string StrategyName => "Cookie";
    public int Priority => 1;
    public StrategyType Type => StrategyType.Manual;

    public Task<bool> CanExecuteAsync()
    {
        var cookie = MiniMaxSettingsReader.GetCookieHeader();
        var hasCookie = !string.IsNullOrEmpty(cookie);
        Log($"CanExecute: hasCookie={hasCookie}");
        return Task.FromResult(hasCookie);
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var rawCookie = MiniMaxSettingsReader.GetCookieHeader();
            if (string.IsNullOrEmpty(rawCookie))
            {
                return new UsageSnapshot
                {
                    ProviderId = "minimax",
                    ErrorMessage = "MiniMax cookie not configured. Paste your cookie header in Settings -> Providers -> MiniMax.",
                    FetchedAt = DateTime.UtcNow
                };
            }

            // Parse the cookie header (supports raw cookie, cURL format, etc.)
            var cookieOverride = MiniMaxCookieHeader.Override(rawCookie);
            if (cookieOverride == null)
            {
                return new UsageSnapshot
                {
                    ProviderId = "minimax",
                    ErrorMessage = "Invalid MiniMax cookie format.",
                    FetchedAt = DateTime.UtcNow
                };
            }

            var usage = await MiniMaxUsageFetcher.FetchUsageAsync(
                cookieOverride.CookieHeader,
                cookieOverride.AuthorizationToken,
                cookieOverride.GroupId,
                cancellationToken);

            return usage;
        }
        catch (MiniMaxUsageException ex)
        {
            Log($"Fetch ERROR: {ex.Message}");
            return new UsageSnapshot
            {
                ProviderId = "minimax",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Log($"Fetch ERROR: {ex.Message}\n{ex.StackTrace}");
            return new UsageSnapshot
            {
                ProviderId = "minimax",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
    }

    private void Log(string message)
    {
        DebugLogger.Log("MiniMaxCookieStrategy", message);
    }
}

/// <summary>
/// Reads and stores MiniMax cookie header securely
/// </summary>
public static class MiniMaxSettingsReader
{
    public const string EnvVarCookie = "MINIMAX_COOKIE";
    public const string EnvVarCookieHeader = "MINIMAX_COOKIE_HEADER";

    /// <summary>
    /// Get cookie header from secure storage or environment variable
    /// Priority: 1. Windows Credential Manager, 2. Environment variables
    /// </summary>
    public static string? GetCookieHeader()
    {
        // First check secure credential store (Windows Credential Manager)
        var cookie = SecureCredentialStore.GetCredential(CredentialKeys.MinimaxCookie);
        if (!string.IsNullOrWhiteSpace(cookie))
            return cookie.Trim();

        // Then check environment variables (for CLI/automation scenarios)
        cookie = Environment.GetEnvironmentVariable(EnvVarCookie);
        if (!string.IsNullOrWhiteSpace(cookie))
            return cookie.Trim();

        cookie = Environment.GetEnvironmentVariable(EnvVarCookieHeader);
        if (!string.IsNullOrWhiteSpace(cookie))
            return cookie.Trim();

        return null;
    }

    /// <summary>
    /// Store cookie header securely in Windows Credential Manager
    /// </summary>
    public static bool StoreCookieHeader(string? cookie)
    {
        var cleaned = cookie?.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            return SecureCredentialStore.DeleteCredential(CredentialKeys.MinimaxCookie);
        
        return SecureCredentialStore.StoreCredential(CredentialKeys.MinimaxCookie, cleaned);
    }

    /// <summary>
    /// Check if cookie header is configured
    /// </summary>
    public static bool HasCookieHeader()
    {
        return !string.IsNullOrWhiteSpace(GetCookieHeader());
    }

    /// <summary>
    /// Delete cookie header from secure storage
    /// </summary>
    public static bool DeleteCookieHeader()
    {
        return SecureCredentialStore.DeleteCredential(CredentialKeys.MinimaxCookie);
    }
}
