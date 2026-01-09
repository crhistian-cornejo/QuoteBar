using System.Net.Http;

namespace QuoteBar.Core.Services;

/// <summary>
/// Shared HttpClient factory to avoid socket exhaustion and reduce memory usage.
/// HttpClient is designed to be instantiated once and reused throughout the app lifecycle.
/// </summary>
public static class SharedHttpClient
{
    private static readonly Lazy<HttpClient> _defaultClient = new(() => new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    });

    private static readonly Lazy<HttpClient> _shortTimeoutClient = new(() => new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(10)
    });

    private static readonly Lazy<HttpClient> _longTimeoutClient = new(() => new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(60)
    });

    /// <summary>
    /// Default HttpClient with 30 second timeout. Use for most API calls.
    /// </summary>
    public static HttpClient Default => _defaultClient.Value;

    /// <summary>
    /// HttpClient with 10 second timeout. Use for quick status checks.
    /// </summary>
    public static HttpClient Quick => _shortTimeoutClient.Value;

    /// <summary>
    /// HttpClient with 60 second timeout. Use for downloads or slow APIs.
    /// </summary>
    public static HttpClient Long => _longTimeoutClient.Value;

    /// <summary>
    /// Create a configured HttpClient with custom default headers.
    /// WARNING: The returned HttpClient MUST be disposed by the caller (use 'using' statement).
    /// Prefer using Default/Quick/Long with per-request headers when possible to avoid creating new instances.
    /// </summary>
    /// <remarks>
    /// This method creates a new HttpClient instance. For better performance and to avoid socket exhaustion,
    /// prefer using SharedHttpClient.Default/Quick/Long and setting headers on individual HttpRequestMessage objects.
    /// </remarks>
    public static HttpClient CreateWithHeaders(params (string name, string value)[] headers)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        foreach (var (name, value) in headers)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
        }
        return client;
    }
}
