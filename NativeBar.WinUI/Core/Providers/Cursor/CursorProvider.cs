using NativeBar.WinUI.Core.Models;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NativeBar.WinUI.Core.Providers.Cursor;

public class CursorProviderDescriptor : ProviderDescriptor
{
    public override string Id => "cursor";
    public override string DisplayName => "Cursor";
    public override string IconGlyph => "\uE8A5"; // Code/Edit icon
    public override string PrimaryColor => "#007AFF";
    public override string SecondaryColor => "#5AC8FA";
    public override string PrimaryLabel => "Daily requests";
    public override string SecondaryLabel => "Monthly limit";

    public override bool SupportsOAuth => true;
    public override bool SupportsCLI => false;

    protected override void InitializeStrategies()
    {
        AddStrategy(new CursorConfigStrategy());
    }
}

/// <summary>
/// Strategy that reads Cursor usage from its config/state files
/// </summary>
public class CursorConfigStrategy : IProviderFetchStrategy
{
    public string StrategyName => "Config";
    public int Priority => 1;

    public async Task<bool> CanExecuteAsync()
    {
        // Check if Cursor config exists
        var configPath = GetCursorConfigPath();
        return await Task.FromResult(System.IO.File.Exists(configPath));
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var configPath = GetCursorConfigPath();

            if (!System.IO.File.Exists(configPath))
            {
                return new UsageSnapshot
                {
                    ProviderId = "cursor",
                    ErrorMessage = "Cursor not configured",
                    FetchedAt = DateTime.UtcNow
                };
            }

            var configContent = await System.IO.File.ReadAllTextAsync(configPath, cancellationToken);
            return ParseCursorConfig(configContent);
        }
        catch (Exception ex)
        {
            return new UsageSnapshot
            {
                ProviderId = "cursor",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
    }

    private string GetCursorConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return System.IO.Path.Combine(appData, "Cursor", "User", "globalStorage", "state.vscdb");
    }

    private UsageSnapshot ParseCursorConfig(string content)
    {
        // TODO: Parse actual Cursor state database
        return new UsageSnapshot
        {
            ProviderId = "cursor",
            Primary = new RateWindow
            {
                UsedPercent = 0,
                WindowMinutes = 1440, // 24 hours
                ResetDescription = "Daily limit"
            },
            Identity = new ProviderIdentity { PlanType = "Pro" },
            FetchedAt = DateTime.UtcNow
        };
    }
}
