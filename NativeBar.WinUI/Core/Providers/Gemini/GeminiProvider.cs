using NativeBar.WinUI.Core.Models;

namespace NativeBar.WinUI.Core.Providers.Gemini;

public class GeminiProviderDescriptor : ProviderDescriptor
{
    public override string Id => "gemini";
    public override string DisplayName => "Gemini";
    public override string IconGlyph => "\uE734"; // Star icon
    public override string PrimaryColor => "#4285F4";
    public override string SecondaryColor => "#8AB4F8";
    public override string PrimaryLabel => "Pro Models";
    public override string SecondaryLabel => "Flash Models";
public override string? DashboardUrl => "https://aistudio.google.com/app/plan";

    public override bool SupportsOAuth => true;
    public override bool SupportsCLI => true;

    protected override void InitializeStrategies()
    {
        // OAuth strategy has higher priority (uses Gemini CLI OAuth credentials)
        AddStrategy(new GeminiOAuthStrategy());
        // CLI fallback using 'gemini /stats' command (lower priority)
        AddStrategy(new GeminiCLIStrategy());
    }
}

/// <summary>
/// OAuth strategy for Google Gemini - uses credentials from Gemini CLI (~/.gemini/)
/// </summary>
public class GeminiOAuthStrategy : IProviderFetchStrategy
{
    public string StrategyName => "OAuth";
    public int Priority => 2; // Higher priority
    public StrategyType Type => StrategyType.OAuth;

    public async Task<bool> CanExecuteAsync()
    {
        // Check if OAuth credentials are available
        return await Task.FromResult(GeminiOAuthCredentialsStore.HasValidCredentials());
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var credentials = GeminiOAuthCredentialsStore.TryLoad();

            if (credentials == null)
            {
                return new UsageSnapshot
                {
                    ProviderId = "gemini",
                    ErrorMessage = "Not configured",
                    FetchedAt = DateTime.UtcNow
                };
            }

            // Fetch usage data from Gemini API
            var usageData = await GeminiUsageFetcher.FetchUsageAsync(credentials, cancellationToken);
            return GeminiUsageFetcher.ToUsageSnapshot(usageData);
        }
        catch (GeminiOAuthCredentialsException ex)
        {
            Log($"Credentials error: {ex.Message}");
            return new UsageSnapshot
            {
                ProviderId = "gemini",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Log($"Fetch error: {ex.Message}");
            return new UsageSnapshot
            {
                ProviderId = "gemini",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
    }

    private static void Log(string message)
    {
        Core.Services.DebugLogger.Log("GeminiOAuthStrategy", message);
    }
}

/// <summary>
/// CLI fallback strategy using 'gemini /stats' command
/// </summary>
public class GeminiCLIStrategy : IProviderFetchStrategy
{
    public string StrategyName => "CLI";
    public int Priority => 1; // Lower priority - fallback
    public StrategyType Type => StrategyType.CLI;

    public async Task<bool> CanExecuteAsync()
    {
        // Check if gemini CLI is available
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "gemini",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
        }
        catch
        {
            // gemini CLI not available
        }

        return false;
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        // CLI fallback is not fully implemented yet
        // For now, return an error directing users to use OAuth
        await Task.CompletedTask;

        return new UsageSnapshot
        {
            ProviderId = "gemini",
            ErrorMessage = "CLI not implemented. Use 'gemini auth login' instead.",
            FetchedAt = DateTime.UtcNow
        };
    }
}
