using NativeBar.WinUI.Core.Models;

namespace NativeBar.WinUI.Core.Providers.Antigravity;

public class AntigravityProviderDescriptor : ProviderDescriptor
{
    public override string Id => "antigravity";
    public override string DisplayName => "Antigravity";
    public override string IconGlyph => "\uE81E"; // Repair icon
    public override string PrimaryColor => "#FF6B6B";
    public override string SecondaryColor => "#FFB3B3";
    public override string PrimaryLabel => "Daily usage";
    public override string SecondaryLabel => "Weekly usage";
    
    public override bool SupportsOAuth => true;
    
    protected override void InitializeStrategies()
    {
        AddStrategy(new AntigravityOAuthStrategy());
    }
}

public class AntigravityOAuthStrategy : IProviderFetchStrategy
{
    public string StrategyName => "OAuth";
    public int Priority => 1;
    
    public async Task<bool> CanExecuteAsync()
    {
        var token = await GetStoredTokenAsync();
        return !string.IsNullOrEmpty(token);
    }
    
    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetStoredTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            return new UsageSnapshot
            {
                ProviderId = "antigravity",
                ErrorMessage = "No Antigravity token available",
                FetchedAt = DateTime.UtcNow
            };
        }
        
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        
        try
        {
            // Antigravity usage endpoint (placeholder)
            var response = await client.GetAsync("https://api.antigravity.dev/v1/usage", cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return new UsageSnapshot
                {
                    ProviderId = "antigravity",
                    ErrorMessage = $"API error: {response.StatusCode}",
                    FetchedAt = DateTime.UtcNow
                };
            }
            
            // TODO: Parse response
            return new UsageSnapshot
            {
                ProviderId = "antigravity",
                Primary = new RateWindow
                {
                    UsedPercent = 0,
                    ResetDescription = "Daily reset"
                },
                Secondary = new RateWindow
                {
                    UsedPercent = 0,
                    ResetDescription = "Weekly reset"
                },
                FetchedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new UsageSnapshot
            {
                ProviderId = "antigravity",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
    }
    
    private async Task<string?> GetStoredTokenAsync()
    {
        // TODO: Implement Windows Credential Manager integration
        return await Task.FromResult<string?>(null);
    }
}
