using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuoteBar.Core.Models;

namespace QuoteBar.Core.Services;

/// <summary>
/// HTTP DelegatingHandler that automatically tracks all requests passing through.
/// Intercepts requests/responses and logs them to RequestTracker.
/// 
/// This is the safest approach for tracking - no system-level hooks, no proxy,
/// just a standard .NET handler in the HTTP pipeline.
/// 
/// Features:
/// - Automatic provider detection from host
/// - Model extraction from path or response
/// - Token usage extraction from response (input_tokens, output_tokens)
/// - Duration tracking
/// </summary>
public sealed class RequestTrackingHandler : DelegatingHandler
{
    // Known AI provider API hosts mapped to provider names
    private static readonly Dictionary<string, string> KnownProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        // Anthropic / Claude
        { "api.anthropic.com", "claude" },
        { "claude.ai", "claude" },
        
        // OpenAI
        { "api.openai.com", "openai" },
        
        // Google / Gemini
        { "generativelanguage.googleapis.com", "gemini" },
        { "aistudio.google.com", "gemini" },
        
        // GitHub Copilot
        { "api.github.com", "copilot" },
        { "copilot-proxy.githubusercontent.com", "copilot" },
        
        // Cursor
        { "api2.cursor.sh", "cursor" },
        { "www.cursor.com", "cursor" },
        { "cursor.com", "cursor" },
        
        // Codex CLI (OpenAI hosted)
        { "api.openai.com/v1/responses", "codex" },
        
        // Augment
        { "api.augmentcode.com", "augment" },
        
        // MiniMax
        { "api.minimax.chat", "minimax" },
    };

    // Paths where we should try to extract token usage from response
    private static readonly HashSet<string> TokenExtractionPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/v1/messages",           // Claude
        "/v1/chat/completions",   // OpenAI, Codex
        "/v1/completions",        // OpenAI legacy
        "/v1/responses",          // Codex CLI
    };

    // Max response size to parse for token extraction (avoid OOM on large responses)
    private const int MaxResponseSizeForParsing = 64 * 1024; // 64KB

    /// <summary>
    /// Whether tracking is enabled. Can be disabled for specific scenarios.
    /// </summary>
    public bool IsTrackingEnabled { get; set; } = true;

    public RequestTrackingHandler() : base(new HttpClientHandler())
    {
    }

    public RequestTrackingHandler(HttpMessageHandler innerHandler) : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!IsTrackingEnabled || request.RequestUri == null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var host = request.RequestUri.Host;
        var path = request.RequestUri.AbsolutePath;

        // Only track AI provider requests
        var provider = DetectProvider(host, path);
        if (provider == null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();
        var requestSize = 0;
        var responseSize = 0;
        HttpResponseMessage? response = null;
        string? errorMessage = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? model = null;

        try
        {
            // Get request size (safely, without consuming the content)
            if (request.Content != null)
            {
                try
                {
                    requestSize = (int)(request.Content.Headers.ContentLength ?? 0);
                }
                catch
                {
                    // Ignore - some content types don't support ContentLength
                }
            }

            // Execute the actual request
            response = await base.SendAsync(request, cancellationToken);

            // Get response size and try to extract tokens
            if (response.Content != null)
            {
                try
                {
                    responseSize = (int)(response.Content.Headers.ContentLength ?? 0);

                    // Try to extract tokens from successful responses
                    if (response.IsSuccessStatusCode && ShouldExtractTokens(path, responseSize))
                    {
                        var tokenInfo = await TryExtractTokenInfoAsync(response, provider);
                        inputTokens = tokenInfo.InputTokens;
                        outputTokens = tokenInfo.OutputTokens;
                        model = tokenInfo.Model;
                    }
                }
                catch
                {
                    // Ignore extraction errors
                }
            }

            return response;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            throw;
        }
        finally
        {
            stopwatch.Stop();

            // Track the request (fire and forget, don't block the response)
            try
            {
                // Try to get model from path if not extracted from response
                model ??= ExtractModelFromPath(path);

                var entry = new RequestLog
                {
                    Timestamp = DateTime.UtcNow,
                    Method = request.Method.Method,
                    Endpoint = path,
                    Provider = provider,
                    Model = model,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    DurationMs = (int)stopwatch.ElapsedMilliseconds,
                    StatusCode = (int?)response?.StatusCode,
                    RequestSize = requestSize,
                    ResponseSize = responseSize,
                    ErrorMessage = errorMessage
                };

                RequestTracker.Instance.AddEntry(entry);
            }
            catch
            {
                // Never let tracking errors affect the actual request
            }
        }
    }

    private static bool ShouldExtractTokens(string path, int responseSize)
    {
        // Only parse small responses from known token-returning endpoints
        if (responseSize > MaxResponseSizeForParsing && responseSize > 0)
            return false;

        foreach (var tokenPath in TokenExtractionPaths)
        {
            if (path.Contains(tokenPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Also check for Gemini-style paths
        if (path.Contains(":generateContent", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static async Task<(int? InputTokens, int? OutputTokens, string? Model)> TryExtractTokenInfoAsync(
        HttpResponseMessage response, string provider)
    {
        try
        {
            // Load response into buffer so we can read it without consuming the stream
            // This is safe because HttpClient will reuse the buffered content
            await response.Content.LoadIntoBufferAsync();

            var json = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(json))
                return (null, null, null);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return provider switch
            {
                "claude" => ExtractClaudeTokens(root),
                "openai" or "codex" => ExtractOpenAITokens(root),
                "gemini" => ExtractGeminiTokens(root),
                _ => TryExtractGenericTokens(root)
            };
        }
        catch
        {
            return (null, null, null);
        }
    }

    /// <summary>
    /// Claude API response format:
    /// { "usage": { "input_tokens": 100, "output_tokens": 50 }, "model": "claude-sonnet-4-20250514" }
    /// </summary>
    private static (int? InputTokens, int? OutputTokens, string? Model) ExtractClaudeTokens(JsonElement root)
    {
        int? input = null, output = null;
        string? model = null;

        if (root.TryGetProperty("model", out var modelProp))
            model = modelProp.GetString();

        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var inputProp))
                input = inputProp.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var outputProp))
                output = outputProp.GetInt32();
        }

        return (input, output, model);
    }

    /// <summary>
    /// OpenAI/Codex API response format:
    /// { "usage": { "prompt_tokens": 100, "completion_tokens": 50 }, "model": "gpt-4" }
    /// </summary>
    private static (int? InputTokens, int? OutputTokens, string? Model) ExtractOpenAITokens(JsonElement root)
    {
        int? input = null, output = null;
        string? model = null;

        if (root.TryGetProperty("model", out var modelProp))
            model = modelProp.GetString();

        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var inputProp))
                input = inputProp.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var outputProp))
                output = outputProp.GetInt32();
        }

        return (input, output, model);
    }

    /// <summary>
    /// Gemini API response format:
    /// { "usageMetadata": { "promptTokenCount": 100, "candidatesTokenCount": 50 } }
    /// Model is in the path, not response
    /// </summary>
    private static (int? InputTokens, int? OutputTokens, string? Model) ExtractGeminiTokens(JsonElement root)
    {
        int? input = null, output = null;

        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            if (usage.TryGetProperty("promptTokenCount", out var inputProp))
                input = inputProp.GetInt32();
            if (usage.TryGetProperty("candidatesTokenCount", out var outputProp))
                output = outputProp.GetInt32();
        }

        return (input, output, null);
    }

    /// <summary>
    /// Generic fallback - try common patterns
    /// </summary>
    private static (int? InputTokens, int? OutputTokens, string? Model) TryExtractGenericTokens(JsonElement root)
    {
        int? input = null, output = null;
        string? model = null;

        if (root.TryGetProperty("model", out var modelProp))
            model = modelProp.GetString();

        // Try usage object
        if (root.TryGetProperty("usage", out var usage))
        {
            // Claude style
            if (usage.TryGetProperty("input_tokens", out var it))
                input = it.GetInt32();
            else if (usage.TryGetProperty("prompt_tokens", out var pt))
                input = pt.GetInt32();

            if (usage.TryGetProperty("output_tokens", out var ot))
                output = ot.GetInt32();
            else if (usage.TryGetProperty("completion_tokens", out var ct))
                output = ct.GetInt32();
        }

        return (input, output, model);
    }

    private static string? DetectProvider(string host, string path)
    {
        // Check known providers
        if (KnownProviders.TryGetValue(host, out var provider))
        {
            return provider;
        }

        // Check if host contains known patterns
        foreach (var (pattern, providerName) in KnownProviders)
        {
            if (host.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return providerName;
            }
        }

        return null;
    }

    private static string? ExtractModelFromPath(string path)
    {
        // Gemini style: /models/gemini-pro:generateContent
        if (path.Contains("/models/", StringComparison.OrdinalIgnoreCase))
        {
            var modelStart = path.IndexOf("/models/", StringComparison.OrdinalIgnoreCase) + 8;
            var modelEnd = path.IndexOf(':', modelStart);
            if (modelEnd == -1) modelEnd = path.IndexOf('/', modelStart);
            if (modelEnd == -1) modelEnd = path.Length;

            if (modelEnd > modelStart)
            {
                return path[modelStart..modelEnd];
            }
        }

        return null;
    }
}
