using QuoteBar.Core.Models;
using QuoteBar.Core.Services;

namespace QuoteBar.Core.Providers.MiniMax;

/// <summary>
/// MiniMax provider descriptor
/// MiniMax usage is fetched from the Coding Plan remains API.
/// Supports two authentication methods:
/// 1. Coding Plan API Key (preferred) - Get from Account/Coding Plan page
/// 2. Session cookie header (fallback) - Copy from browser DevTools
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
        // Try API Key first (more reliable), then fall back to cookies
        AddStrategy(new MiniMaxApiKeyStrategy());
        AddStrategy(new MiniMaxCookieStrategy());
    }
}

/// <summary>
/// API Key-based strategy for MiniMax Coding Plan
/// Uses Coding Plan API Key from Account/Coding Plan page
/// This is the preferred method as it's more reliable than cookies
/// </summary>
public class MiniMaxApiKeyStrategy : IProviderFetchStrategy
{
    public string StrategyName => "API Key";
    public int Priority => 10; // Higher priority than cookies
    public StrategyType Type => StrategyType.Manual;

    public Task<bool> CanExecuteAsync()
    {
        var apiKey = MiniMaxSettingsReader.GetApiKey();
        var hasApiKey = !string.IsNullOrEmpty(apiKey);
        Log($"CanExecute: hasApiKey={hasApiKey}");
        return Task.FromResult(hasApiKey);
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Log("FetchAsync called (API Key strategy)");
            var apiKey = MiniMaxSettingsReader.GetApiKey();
            
            if (string.IsNullOrEmpty(apiKey))
            {
                Log("No API key configured");
                return new UsageSnapshot
                {
                    ProviderId = "minimax",
                    ErrorMessage = "MiniMax API key not configured. Get your Coding Plan API Key from Account/Coding Plan page.",
                    FetchedAt = DateTime.UtcNow
                };
            }

            var groupId = MiniMaxSettingsReader.GetGroupId();
            Log($"Using API Key: {apiKey.Substring(0, Math.Min(8, apiKey.Length))}..., groupId={groupId ?? "none"}");

            var usage = await MiniMaxUsageFetcher.FetchUsageWithApiKeyAsync(
                apiKey,
                groupId,
                cancellationToken);

            Log($"Fetch result: {(usage.ErrorMessage != null ? $"Error: {usage.ErrorMessage}" : $"Success - {usage.Primary?.UsedPercent}% used")}");
            return usage;
        }
        catch (MiniMaxUsageException ex)
        {
            Log($"Fetch MiniMaxUsageException: {ex.Message}");
            var isAuthError = ex.ErrorType == MiniMaxErrorType.InvalidCredentials;
            return new UsageSnapshot
            {
                ProviderId = "minimax",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow,
                RequiresReauth = isAuthError
            };
        }
        catch (Exception ex)
        {
            Log($"Fetch Exception: {ex.Message}\n{ex.StackTrace}");
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
        DebugLogger.Log("MiniMaxApiKeyStrategy", message);
    }
}

/// <summary>
/// Cookie-based strategy for MiniMax
/// Uses manual cookie header or environment variable
/// Falls back to this if API Key is not available
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
            Log("FetchAsync called");
            var rawCookie = MiniMaxSettingsReader.GetCookieHeader();
            Log($"Raw cookie from storage: {(rawCookie != null ? $"{rawCookie.Length} chars" : "NULL")}");

            if (string.IsNullOrEmpty(rawCookie))
            {
                Log("No cookie configured");
                return new UsageSnapshot
                {
                    ProviderId = "minimax",
                    ErrorMessage = "MiniMax cookie not configured. Click Connect to sign in.",
                    FetchedAt = DateTime.UtcNow
                };
            }

            // Parse the cookie header (supports raw cookie, cURL format, etc.)
            var cookieOverride = MiniMaxCookieHeader.Override(rawCookie);
            if (cookieOverride == null)
            {
                Log("Cookie parse failed");
                return new UsageSnapshot
                {
                    ProviderId = "minimax",
                    ErrorMessage = "Invalid MiniMax cookie format.",
                    FetchedAt = DateTime.UtcNow
                };
            }

            Log($"Parsed cookie: {cookieOverride.CookieHeader.Length} chars, auth={cookieOverride.AuthorizationToken != null}, groupId={cookieOverride.GroupId}");

            var usage = await MiniMaxUsageFetcher.FetchUsageAsync(
                cookieOverride.CookieHeader,
                cookieOverride.AuthorizationToken,
                cookieOverride.GroupId,
                cancellationToken);

            Log($"Fetch result: {(usage.ErrorMessage != null ? $"Error: {usage.ErrorMessage}" : $"Success - {usage.Primary?.UsedPercent}% used")}");
            return usage;
        }
        catch (MiniMaxUsageException ex)
        {
            Log($"Fetch MiniMaxUsageException: {ex.Message}");
            var isAuthError = ex.ErrorType == MiniMaxErrorType.InvalidCredentials;
            return new UsageSnapshot
            {
                ProviderId = "minimax",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow,
                RequiresReauth = isAuthError
            };
        }
        catch (Exception ex)
        {
            Log($"Fetch Exception: {ex.Message}\n{ex.StackTrace}");
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
/// Reads and stores MiniMax credentials securely
/// Supports both API Key (preferred) and Cookie (fallback) methods
/// </summary>
public static class MiniMaxSettingsReader
{
    public const string EnvVarCookie = "MINIMAX_COOKIE";
    public const string EnvVarCookieHeader = "MINIMAX_COOKIE_HEADER";
    public const string EnvVarApiKey = "MINIMAX_API_KEY";
    public const string EnvVarGroupId = "MINIMAX_GROUP_ID";

    /// <summary>
    /// Get Coding Plan API Key from secure storage or environment variable
    /// Priority: 1. Windows Credential Manager, 2. Environment variable
    /// </summary>
    public static string? GetApiKey()
    {
        // First check secure credential store (Windows Credential Manager)
        var apiKey = SecureCredentialStore.GetCredential(CredentialKeys.MinimaxApiKey);
        if (!string.IsNullOrWhiteSpace(apiKey))
            return CleanApiKey(apiKey);

        // Then check environment variable (for CLI/automation scenarios)
        apiKey = Environment.GetEnvironmentVariable(EnvVarApiKey);
        if (!string.IsNullOrWhiteSpace(apiKey))
            return CleanApiKey(apiKey);

        return null;
    }

    /// <summary>
    /// Store Coding Plan API Key securely in Windows Credential Manager
    /// </summary>
    public static bool StoreApiKey(string? apiKey)
    {
        var cleaned = CleanApiKey(apiKey);
        if (string.IsNullOrWhiteSpace(cleaned))
            return SecureCredentialStore.DeleteCredential(CredentialKeys.MinimaxApiKey);
        
        return SecureCredentialStore.StoreCredential(CredentialKeys.MinimaxApiKey, cleaned);
    }

    /// <summary>
    /// Check if API Key is configured
    /// </summary>
    public static bool HasApiKey()
    {
        return !string.IsNullOrWhiteSpace(GetApiKey());
    }

    /// <summary>
    /// Delete API Key from secure storage
    /// </summary>
    public static bool DeleteApiKey()
    {
        return SecureCredentialStore.DeleteCredential(CredentialKeys.MinimaxApiKey);
    }

    /// <summary>
    /// Get Group ID from secure storage or environment variable
    /// </summary>
    public static string? GetGroupId()
    {
        var groupId = SecureCredentialStore.GetCredential(CredentialKeys.MinimaxGroupId);
        if (!string.IsNullOrWhiteSpace(groupId))
            return groupId.Trim();

        groupId = Environment.GetEnvironmentVariable(EnvVarGroupId);
        if (!string.IsNullOrWhiteSpace(groupId))
            return groupId.Trim();

        return null;
    }

    /// <summary>
    /// Store Group ID securely
    /// </summary>
    public static bool StoreGroupId(string? groupId)
    {
        var cleaned = groupId?.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            return SecureCredentialStore.DeleteCredential(CredentialKeys.MinimaxGroupId);
        
        return SecureCredentialStore.StoreCredential(CredentialKeys.MinimaxGroupId, cleaned);
    }

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

    private static string? CleanApiKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();

        // Remove surrounding quotes if present
        if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
            (value.StartsWith("'") && value.EndsWith("'")))
        {
            value = value.Substring(1, value.Length - 2);
        }

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
