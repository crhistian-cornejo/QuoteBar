using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace QuoteBar.Core.CostUsage;

/// <summary>
/// Token pricing calculations for Codex and Claude models
/// Based on CodexBar's CostUsagePricing.swift
/// </summary>
public static class CostUsagePricing
{
    private record CodexPricing(
        double InputCostPerToken,
        double OutputCostPerToken,
        double CacheReadInputCostPerToken);

    private record ClaudePricing(
        double InputCostPerToken,
        double OutputCostPerToken,
        double CacheCreationInputCostPerToken,
        double CacheReadInputCostPerToken,
        int? ThresholdTokens = null,
        double? InputCostPerTokenAboveThreshold = null,
        double? OutputCostPerTokenAboveThreshold = null,
        double? CacheCreationInputCostPerTokenAboveThreshold = null,
        double? CacheReadInputCostPerTokenAboveThreshold = null);

    private record CopilotPricing(
        double CostPerPremiumRequest,
        string DisplayName);

    // Codex/OpenAI model pricing
    private static readonly Dictionary<string, CodexPricing> _codexPricing = new()
    {
        ["gpt-5"] = new(1.25e-6, 1e-5, 1.25e-7),
        ["gpt-5-codex"] = new(1.25e-6, 1e-5, 1.25e-7),
        ["gpt-5.1"] = new(1.25e-6, 1e-5, 1.25e-7),
        ["gpt-5.2"] = new(1.75e-6, 1.4e-5, 1.75e-7),
        ["gpt-5.2-codex"] = new(1.75e-6, 1.4e-5, 1.75e-7),
        // GPT-4 series
        ["gpt-4o"] = new(2.5e-6, 10e-6, 1.25e-6),
        ["gpt-4o-mini"] = new(0.15e-6, 0.6e-6, 0.075e-6),
        ["gpt-4-turbo"] = new(10e-6, 30e-6, 5e-6),
        ["gpt-4"] = new(30e-6, 60e-6, 15e-6),
        // o1/o3 series
        ["o1"] = new(15e-6, 60e-6, 7.5e-6),
        ["o1-mini"] = new(3e-6, 12e-6, 1.5e-6),
        ["o3"] = new(10e-6, 40e-6, 5e-6),
        ["o3-mini"] = new(1.1e-6, 4.4e-6, 0.55e-6),
    };

    // GitHub Copilot model pricing (premium requests cost estimate)
    // Based on GitHub's billing for premium requests at ~$0.04/request average
    private static readonly Dictionary<string, CopilotPricing> _copilotPricing = new(StringComparer.OrdinalIgnoreCase)
    {
        // Claude models via Copilot
        ["Claude Opus 4.5"] = new(0.08, "Opus 4.5"),
        ["Claude Sonnet 4.5"] = new(0.04, "Sonnet 4.5"),
        ["Claude Sonnet 3.5"] = new(0.03, "Sonnet 3.5"),
        ["Claude 3.5 Sonnet"] = new(0.03, "Sonnet 3.5"),
        ["Claude 3 Opus"] = new(0.06, "Opus 3"),
        
        // OpenAI models via Copilot
        ["GPT-4o"] = new(0.02, "GPT-4o"),
        ["GPT-4"] = new(0.04, "GPT-4"),
        ["GPT-5"] = new(0.05, "GPT-5"),
        ["o1"] = new(0.06, "o1"),
        ["o1-preview"] = new(0.06, "o1"),
        ["o1-mini"] = new(0.03, "o1-mini"),
        ["o3"] = new(0.08, "o3"),
        ["o3-mini"] = new(0.04, "o3-mini"),
        
        // Gemini models via Copilot
        ["Gemini 2.5 Pro"] = new(0.04, "Gemini 2.5"),
        ["Gemini 2.0 Flash"] = new(0.02, "Gemini Flash"),
        ["Gemini 3 Flash"] = new(0.02, "Gemini Flash"),
        ["Gemini 3 Pro"] = new(0.04, "Gemini Pro"),
        ["Gemini Flash"] = new(0.02, "Gemini Flash"),
        
        // Default/unknown
        ["default"] = new(0.04, "Unknown"),
    };

    // Claude model pricing
    private static readonly Dictionary<string, ClaudePricing> _claudePricing = new()
    {
        // Claude 4.5 series
        ["claude-haiku-4-5-20251001"] = new(
            InputCostPerToken: 1e-6,
            OutputCostPerToken: 5e-6,
            CacheCreationInputCostPerToken: 1.25e-6,
            CacheReadInputCostPerToken: 1e-7),
        ["claude-haiku-4-5"] = new(
            InputCostPerToken: 1e-6,
            OutputCostPerToken: 5e-6,
            CacheCreationInputCostPerToken: 1.25e-6,
            CacheReadInputCostPerToken: 1e-7),
        ["claude-opus-4-5-20251101"] = new(
            InputCostPerToken: 5e-6,
            OutputCostPerToken: 2.5e-5,
            CacheCreationInputCostPerToken: 6.25e-6,
            CacheReadInputCostPerToken: 5e-7),
        ["claude-opus-4-5"] = new(
            InputCostPerToken: 5e-6,
            OutputCostPerToken: 2.5e-5,
            CacheCreationInputCostPerToken: 6.25e-6,
            CacheReadInputCostPerToken: 5e-7),
        ["claude-sonnet-4-5"] = new(
            InputCostPerToken: 3e-6,
            OutputCostPerToken: 1.5e-5,
            CacheCreationInputCostPerToken: 3.75e-6,
            CacheReadInputCostPerToken: 3e-7,
            ThresholdTokens: 200_000,
            InputCostPerTokenAboveThreshold: 6e-6,
            OutputCostPerTokenAboveThreshold: 2.25e-5,
            CacheCreationInputCostPerTokenAboveThreshold: 7.5e-6,
            CacheReadInputCostPerTokenAboveThreshold: 6e-7),
        ["claude-sonnet-4-5-20250929"] = new(
            InputCostPerToken: 3e-6,
            OutputCostPerToken: 1.5e-5,
            CacheCreationInputCostPerToken: 3.75e-6,
            CacheReadInputCostPerToken: 3e-7,
            ThresholdTokens: 200_000,
            InputCostPerTokenAboveThreshold: 6e-6,
            OutputCostPerTokenAboveThreshold: 2.25e-5,
            CacheCreationInputCostPerTokenAboveThreshold: 7.5e-6,
            CacheReadInputCostPerTokenAboveThreshold: 6e-7),
        // Claude 4 series
        ["claude-opus-4-20250514"] = new(
            InputCostPerToken: 1.5e-5,
            OutputCostPerToken: 7.5e-5,
            CacheCreationInputCostPerToken: 1.875e-5,
            CacheReadInputCostPerToken: 1.5e-6),
        ["claude-opus-4-1"] = new(
            InputCostPerToken: 1.5e-5,
            OutputCostPerToken: 7.5e-5,
            CacheCreationInputCostPerToken: 1.875e-5,
            CacheReadInputCostPerToken: 1.5e-6),
        ["claude-sonnet-4-20250514"] = new(
            InputCostPerToken: 3e-6,
            OutputCostPerToken: 1.5e-5,
            CacheCreationInputCostPerToken: 3.75e-6,
            CacheReadInputCostPerToken: 3e-7,
            ThresholdTokens: 200_000,
            InputCostPerTokenAboveThreshold: 6e-6,
            OutputCostPerTokenAboveThreshold: 2.25e-5,
            CacheCreationInputCostPerTokenAboveThreshold: 7.5e-6,
            CacheReadInputCostPerTokenAboveThreshold: 6e-7),
        // Claude 3.5 series
        ["claude-3-5-sonnet-20241022"] = new(
            InputCostPerToken: 3e-6,
            OutputCostPerToken: 1.5e-5,
            CacheCreationInputCostPerToken: 3.75e-6,
            CacheReadInputCostPerToken: 3e-7),
        ["claude-3-5-sonnet"] = new(
            InputCostPerToken: 3e-6,
            OutputCostPerToken: 1.5e-5,
            CacheCreationInputCostPerToken: 3.75e-6,
            CacheReadInputCostPerToken: 3e-7),
        ["claude-3-5-haiku"] = new(
            InputCostPerToken: 1e-6,
            OutputCostPerToken: 5e-6,
            CacheCreationInputCostPerToken: 1.25e-6,
            CacheReadInputCostPerToken: 1e-7),
        // Claude 3 series
        ["claude-3-opus"] = new(
            InputCostPerToken: 1.5e-5,
            OutputCostPerToken: 7.5e-5,
            CacheCreationInputCostPerToken: 1.875e-5,
            CacheReadInputCostPerToken: 1.5e-6),
        ["claude-3-sonnet"] = new(
            InputCostPerToken: 3e-6,
            OutputCostPerToken: 1.5e-5,
            CacheCreationInputCostPerToken: 3.75e-6,
            CacheReadInputCostPerToken: 3e-7),
        ["claude-3-haiku"] = new(
            InputCostPerToken: 2.5e-7,
            OutputCostPerToken: 1.25e-6,
            CacheCreationInputCostPerToken: 3e-7,
            CacheReadInputCostPerToken: 3e-8),
    };

    /// <summary>
    /// Normalize Codex model name (strip provider prefix, etc.)
    /// </summary>
    public static string NormalizeCodexModel(string raw)
    {
        var trimmed = raw.Trim();
        
        if (trimmed.StartsWith("openai/", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[7..];
        
        // Check if base model (without -codex suffix) exists in pricing
        var codexSuffixIndex = trimmed.IndexOf("-codex", StringComparison.OrdinalIgnoreCase);
        if (codexSuffixIndex > 0)
        {
            var baseModel = trimmed[..codexSuffixIndex];
            if (_codexPricing.ContainsKey(baseModel))
                return baseModel;
        }

        return trimmed;
    }

    /// <summary>
    /// Normalize Claude model name (strip anthropic. prefix, version suffixes, etc.)
    /// </summary>
    public static string NormalizeClaudeModel(string raw)
    {
        var trimmed = raw.Trim();

        if (trimmed.StartsWith("anthropic.", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[10..];

        // Handle Vertex AI format: claude-opus-4-5@20251101
        if (trimmed.Contains('@'))
        {
            var atIndex = trimmed.IndexOf('@');
            trimmed = trimmed[..atIndex];
        }

        // Remove version suffix like -v1:0
        var vMatch = Regex.Match(trimmed, @"-v\d+:\d+$");
        if (vMatch.Success)
            trimmed = trimmed[..vMatch.Index];

        // Check if base model (without date suffix) exists in pricing
        var dateMatch = Regex.Match(trimmed, @"-\d{8}$");
        if (dateMatch.Success)
        {
            var baseModel = trimmed[..dateMatch.Index];
            if (_claudePricing.ContainsKey(baseModel))
                return baseModel;
        }

        return trimmed;
    }

    /// <summary>
    /// Calculate cost in USD for Codex usage
    /// </summary>
    public static double? CodexCostUSD(string model, int inputTokens, int cachedInputTokens, int outputTokens)
    {
        var key = NormalizeCodexModel(model);
        if (!_codexPricing.TryGetValue(key, out var pricing))
            return null;

        var cached = Math.Min(Math.Max(0, cachedInputTokens), Math.Max(0, inputTokens));
        var nonCached = Math.Max(0, inputTokens - cached);

        return nonCached * pricing.InputCostPerToken
            + cached * pricing.CacheReadInputCostPerToken
            + Math.Max(0, outputTokens) * pricing.OutputCostPerToken;
    }

    /// <summary>
    /// Calculate cost in USD for Claude usage
    /// </summary>
    public static double? ClaudeCostUSD(
        string model,
        int inputTokens,
        int cacheReadInputTokens,
        int cacheCreationInputTokens,
        int outputTokens)
    {
        var key = NormalizeClaudeModel(model);
        if (!_claudePricing.TryGetValue(key, out var pricing))
            return null;

        static double Tiered(int tokens, double basePrice, double? abovePrice, int? threshold)
        {
            if (threshold == null || abovePrice == null)
                return tokens * basePrice;

            var below = Math.Min(tokens, threshold.Value);
            var above = Math.Max(tokens - threshold.Value, 0);
            return below * basePrice + above * abovePrice.Value;
        }

        return Tiered(Math.Max(0, inputTokens), pricing.InputCostPerToken,
                     pricing.InputCostPerTokenAboveThreshold, pricing.ThresholdTokens)
            + Tiered(Math.Max(0, cacheReadInputTokens), pricing.CacheReadInputCostPerToken,
                     pricing.CacheReadInputCostPerTokenAboveThreshold, pricing.ThresholdTokens)
            + Tiered(Math.Max(0, cacheCreationInputTokens), pricing.CacheCreationInputCostPerToken,
                     pricing.CacheCreationInputCostPerTokenAboveThreshold, pricing.ThresholdTokens)
            + Tiered(Math.Max(0, outputTokens), pricing.OutputCostPerToken,
                     pricing.OutputCostPerTokenAboveThreshold, pricing.ThresholdTokens);
    }

    /// <summary>
    /// Calculate estimated cost in USD for Copilot premium request usage
    /// </summary>
    public static double? CopilotCostUSD(string model, double requestCount)
    {
        if (requestCount <= 0)
            return null;

        var pricing = GetCopilotPricing(model);
        return requestCount * pricing.CostPerPremiumRequest;
    }

    /// <summary>
    /// Get the pricing info for a Copilot model
    /// </summary>
    private static CopilotPricing GetCopilotPricing(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return _copilotPricing["default"];

        // Try exact match first
        if (_copilotPricing.TryGetValue(model, out var exact))
            return exact;

        // Try partial match
        var lowerModel = model.ToLowerInvariant();
        foreach (var (key, pricing) in _copilotPricing)
        {
            if (lowerModel.Contains(key.ToLowerInvariant()))
                return pricing;
        }

        return _copilotPricing["default"];
    }

    /// <summary>
    /// Get a display-friendly name for a Copilot model
    /// </summary>
    public static string CopilotModelDisplayName(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return "Unknown";

        var pricing = GetCopilotPricing(model);
        return pricing.DisplayName;
    }

    /// <summary>
    /// Get a display-friendly model name
    /// </summary>
    public static string ModelDisplayName(string model)
    {
        var normalized = model.Trim();
        
        // Remove common prefixes
        if (normalized.StartsWith("openai/"))
            normalized = normalized[7..];
        if (normalized.StartsWith("anthropic."))
            normalized = normalized[10..];

        // Remove date suffixes for display
        var dateMatch = Regex.Match(normalized, @"-\d{8}$");
        if (dateMatch.Success)
            normalized = normalized[..dateMatch.Index];

        return normalized;
    }
}
