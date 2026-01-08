using QuoteBar.Core.Models;

namespace QuoteBar.Core.Providers.GitHub;

public class GitHubProviderDescriptor : ProviderDescriptor
{
    public override string Id => "github";
    public override string DisplayName => "GitHub Copilot";
    public override string IconGlyph => "\uE943"; // Code icon
    public override string PrimaryColor => "#238636";
    public override string SecondaryColor => "#2EA043";
    public override string PrimaryLabel => "Monthly usage";
    public override string SecondaryLabel => "Completions";
    
    public override bool SupportsOAuth => true;
    
    protected override void InitializeStrategies()
    {
        AddStrategy(new GitHubOAuthStrategy());
    }
}

public class GitHubOAuthStrategy : IProviderFetchStrategy
{
    public string StrategyName => "OAuth";
    public int Priority => 1;
    public StrategyType Type => StrategyType.OAuth;

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
                ProviderId = "github",
                ErrorMessage = "No GitHub token available",
                FetchedAt = DateTime.UtcNow
            };
        }
        
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"token {token}");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        client.DefaultRequestHeaders.Add("User-Agent", "QuoteBar");
        
        try
        {
            // GitHub Copilot usage endpoint
            var response = await client.GetAsync("https://api.github.com/copilot/usage", cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return new UsageSnapshot
                {
                    ProviderId = "github",
                    ErrorMessage = $"GitHub API error: {response.StatusCode}",
                    FetchedAt = DateTime.UtcNow
                };
            }
            
            // TODO: Parse GitHub response
            return new UsageSnapshot
            {
                ProviderId = "github",
                Primary = new RateWindow
                {
                    UsedPercent = 0,
                    ResetDescription = "Monthly reset"
                },
                FetchedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new UsageSnapshot
            {
                ProviderId = "github",
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
