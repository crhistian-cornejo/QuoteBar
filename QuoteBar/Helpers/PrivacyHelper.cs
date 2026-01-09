using System.Text.RegularExpressions;
using QuoteBar.Core.Services;

namespace QuoteBar.Helpers;

/// <summary>
/// Helper for privacy-related functions like masking emails
/// </summary>
public static class PrivacyHelper
{
    /// <summary>
    /// Masks an email address for privacy. 
    /// Example: "john.doe@example.com" -> "j***@***.com"
    /// </summary>
    /// <param name="email">The email to mask</param>
    /// <returns>Masked email if privacy mode is enabled, otherwise original email</returns>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email))
            return string.Empty;

        // Only mask if privacy mode is enabled
        if (!SettingsService.Instance.Settings.PrivacyModeEnabled)
            return email;

        return MaskEmailInternal(email);
    }

    /// <summary>
    /// Forces email masking regardless of settings (for use in notifications, etc.)
    /// </summary>
    public static string ForceeMaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email))
            return string.Empty;

        return MaskEmailInternal(email);
    }

    private static string MaskEmailInternal(string email)
    {
        // Pattern: show first char, mask rest before @, mask domain except TLD
        // Example: john.doe@example.com -> j***@***.com

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
        {
            // Not a valid email, just mask most of it
            if (email.Length <= 2)
                return new string('*', email.Length);
            return email[0] + new string('*', email.Length - 1);
        }

        var localPart = email[..atIndex];
        var domainPart = email[(atIndex + 1)..];

        // Mask local part: keep first char, mask rest
        var maskedLocal = localPart.Length > 1 
            ? localPart[0] + "***" 
            : localPart;

        // Mask domain: keep TLD only
        var lastDot = domainPart.LastIndexOf('.');
        string maskedDomain;
        if (lastDot > 0)
        {
            var tld = domainPart[lastDot..]; // .com, .org, etc.
            maskedDomain = "***" + tld;
        }
        else
        {
            maskedDomain = "***";
        }

        return $"{maskedLocal}@{maskedDomain}";
    }

    /// <summary>
    /// Masks any email addresses found in a text string
    /// </summary>
    public static string MaskEmailsInText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (!SettingsService.Instance.Settings.PrivacyModeEnabled)
            return text;

        // Simple email regex pattern
        var emailPattern = @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}";
        return Regex.Replace(text, emailPattern, match => MaskEmailInternal(match.Value));
    }
}
