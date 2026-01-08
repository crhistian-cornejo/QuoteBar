using System.Text.RegularExpressions;

namespace QuoteBar.Core.Providers.MiniMax;

/// <summary>
/// Represents a parsed MiniMax cookie override with optional authorization and group ID
/// </summary>
public class MiniMaxCookieOverride
{
    public string CookieHeader { get; }
    public string? AuthorizationToken { get; }
    public string? GroupId { get; }

    public MiniMaxCookieOverride(string cookieHeader, string? authorizationToken, string? groupId)
    {
        CookieHeader = cookieHeader;
        AuthorizationToken = authorizationToken;
        GroupId = groupId;
    }
}

/// <summary>
/// Parses and normalizes MiniMax cookie headers from various formats
/// Supports raw cookie values, cURL commands, and header formats
/// Based on CodexBar's MiniMaxCookieHeader.swift implementation
/// </summary>
public static class MiniMaxCookieHeader
{
    // Patterns to extract cookie from various formats (cURL, headers, etc.)
    private static readonly string[] HeaderPatterns = {
        @"(?i)-H\s*'Cookie:\s*([^']+)'",
        @"(?i)-H\s*""Cookie:\s*([^""]+)""",
        @"(?i)\bcookie:\s*'([^']+)'",
        @"(?i)\bcookie:\s*""([^""]+)""",
        @"(?i)\bcookie:\s*([^\r\n]+)",
        @"(?i)(?:--cookie|-b)\s*'([^']+)'",
        @"(?i)(?:--cookie|-b)\s*""([^""]+)""",
        @"(?i)(?:--cookie|-b)\s*([^\s]+)"
    };

    private const string AuthorizationPattern = @"(?i)\bauthorization:\s*bearer\s+([A-Za-z0-9._\-+=/]+)";
    private const string GroupIdPattern = @"(?i)\bgroup[_]?id=([0-9]{4,})";

    /// <summary>
    /// Parse a raw string (cookie header, cURL command, etc.) and extract
    /// the cookie header along with optional authorization token and group ID
    /// </summary>
    public static MiniMaxCookieOverride? Override(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        var cookie = Normalized(trimmed);
        
        if (string.IsNullOrEmpty(cookie))
            return null;

        var authorizationToken = ExtractFirst(AuthorizationPattern, trimmed);
        var groupId = ExtractFirst(GroupIdPattern, trimmed);

        return new MiniMaxCookieOverride(cookie, authorizationToken, groupId);
    }

    /// <summary>
    /// Normalize a raw cookie string by extracting just the cookie value
    /// from various formats (cURL, headers, raw value)
    /// </summary>
    public static string? Normalized(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();

        // Try to extract from cURL or header format
        var extracted = ExtractHeader(value);
        if (!string.IsNullOrEmpty(extracted))
        {
            value = extracted;
        }

        // Strip "Cookie:" prefix if present
        value = StripCookiePrefix(value);

        // Strip wrapping quotes
        value = StripWrappingQuotes(value);

        value = value.Trim();

        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// Try to extract cookie value from cURL command or header format
    /// </summary>
    private static string? ExtractHeader(string raw)
    {
        foreach (var pattern in HeaderPatterns)
        {
            try
            {
                var match = Regex.Match(raw, pattern);
                if (match.Success && match.Groups.Count > 1)
                {
                    var captured = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(captured))
                        return captured;
                }
            }
            catch
            {
                // Ignore regex errors
            }
        }

        return null;
    }

    /// <summary>
    /// Strip "Cookie:" prefix if present
    /// </summary>
    private static string StripCookiePrefix(string raw)
    {
        var trimmed = raw.Trim();
        
        if (trimmed.StartsWith("cookie:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Substring("cookie:".Length).Trim();
        }

        return trimmed;
    }

    /// <summary>
    /// Strip wrapping single or double quotes
    /// </summary>
    private static string StripWrappingQuotes(string raw)
    {
        if (raw.Length < 2)
            return raw;

        if ((raw.StartsWith("\"") && raw.EndsWith("\"")) ||
            (raw.StartsWith("'") && raw.EndsWith("'")))
        {
            return raw.Substring(1, raw.Length - 2);
        }

        return raw;
    }

    /// <summary>
    /// Extract the first match of a regex pattern
    /// </summary>
    private static string? ExtractFirst(string pattern, string text)
    {
        try
        {
            var match = Regex.Match(text, pattern);
            if (match.Success && match.Groups.Count > 1)
            {
                var value = match.Groups[1].Value.Trim();
                return string.IsNullOrEmpty(value) ? null : value;
            }
        }
        catch
        {
            // Ignore regex errors
        }

        return null;
    }
}
