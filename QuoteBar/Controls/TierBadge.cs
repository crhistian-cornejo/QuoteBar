using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace QuoteBar.Controls;

/// <summary>
/// A colored badge that displays the subscription tier (Pro, Max, Ultra, Free, etc.)
/// Styled to match Quotio's SubscriptionBadgeV2
/// </summary>
public sealed class TierBadge : UserControl
{
    private readonly Border _border;
    private readonly TextBlock _text;

    /// <summary>
    /// Tier color configurations
    /// </summary>
    private static readonly Dictionary<string, (Color Background, Color Text)> TierColors = new(StringComparer.OrdinalIgnoreCase)
    {
        // Ultra - Gold/Amber (highest tier)
        { "ultra", (Color.FromArgb(255, 255, 242, 204), Color.FromArgb(255, 133, 100, 5)) },
        
        // Max - Purple (Claude Max, high tier)
        { "max", (Color.FromArgb(255, 237, 220, 255), Color.FromArgb(255, 102, 51, 153)) },
        
        // Pro - Blue
        { "pro", (Color.FromArgb(255, 204, 230, 255), Color.FromArgb(255, 0, 64, 133)) },
        
        // Plus - Green (OpenAI Plus)
        { "plus", (Color.FromArgb(255, 209, 250, 229), Color.FromArgb(255, 21, 128, 61)) },
        
        // Team - Teal
        { "team", (Color.FromArgb(255, 204, 251, 241), Color.FromArgb(255, 13, 148, 136)) },
        
        // Enterprise - Dark blue
        { "enterprise", (Color.FromArgb(255, 219, 234, 254), Color.FromArgb(255, 29, 78, 216)) },
        
        // Business - Indigo
        { "business", (Color.FromArgb(255, 224, 231, 255), Color.FromArgb(255, 67, 56, 202)) },
        
        // Free/Standard - Gray (lowest tier)
        { "free", (Color.FromArgb(255, 232, 236, 239), Color.FromArgb(255, 107, 117, 125)) },
        { "standard", (Color.FromArgb(255, 232, 236, 239), Color.FromArgb(255, 107, 117, 125)) },
        
        // Default fallback
        { "default", (Color.FromArgb(255, 232, 236, 239), Color.FromArgb(255, 107, 117, 125)) }
    };

    public TierBadge()
    {
        _text = new TextBlock
        {
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Capsule shape
        _border = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 3, 8, 3),
            Child = _text
        };

        Content = _border;
    }

    /// <summary>
    /// Creates a TierBadge with the specified plan type
    /// </summary>
    public TierBadge(string? planType) : this()
    {
        SetTier(planType);
    }

    /// <summary>
    /// Set the tier to display
    /// </summary>
    public void SetTier(string? planType)
    {
        if (string.IsNullOrWhiteSpace(planType))
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        
        // Extract display name and determine colors
        var (displayName, bgColor, textColor) = GetTierConfig(planType);
        
        _text.Text = displayName;
        _text.Foreground = new SolidColorBrush(textColor);
        _border.Background = new SolidColorBrush(bgColor);
    }

    /// <summary>
    /// Get tier configuration (display name, background color, text color)
    /// </summary>
    private static (string DisplayName, Color Background, Color Text) GetTierConfig(string planType)
    {
        var lower = planType.ToLowerInvariant();
        
        // Check specific patterns first (order matters - more specific patterns first)
        // Pro+ / Pro Plus variants - use Plus colors (green) to distinguish from Pro
        if (lower.Contains("pro+") || lower.Contains("pro plus") || lower.Contains("pro_plus"))
        {
            var plusColors = TierColors["plus"];
            return ("Pro+", plusColors.Background, plusColors.Text);
        }
        
        // Check for each tier keyword in priority order
        var tiersInOrder = new[] { "ultra", "max", "enterprise", "business", "team", "pro", "plus", "free", "standard" };
        
        foreach (var tier in tiersInOrder)
        {
            if (lower.Contains(tier))
            {
                var colors = TierColors[tier];
                var displayName = GetCleanDisplayName(planType, tier);
                return (displayName, colors.Background, colors.Text);
            }
        }
        
        // Fallback: use the original text with default colors
        var defaultColors = TierColors["default"];
        return (planType, defaultColors.Background, defaultColors.Text);
    }

    /// <summary>
    /// Get a clean display name for the tier
    /// </summary>
    private static string GetCleanDisplayName(string planType, string matchedTier)
    {
        // Handle common patterns
        var lower = planType.ToLowerInvariant();
        
        // "Max (Level 5)" -> "Max L5"
        if (lower.Contains("level 5") || lower.Contains("level5"))
            return "Max L5";
        if (lower.Contains("level 4") || lower.Contains("level4"))
            return "Max L4";
        
        // Just capitalize the matched tier
        return matchedTier switch
        {
            "ultra" => "Ultra",
            "max" => "Max",
            "pro" => "Pro",
            "plus" => "Plus",
            "team" => "Team",
            "enterprise" => "Enterprise",
            "business" => "Business",
            "free" => "Free",
            "standard" => "Free",
            _ => char.ToUpper(matchedTier[0]) + matchedTier[1..]
        };
    }

    /// <summary>
    /// Static factory method to create a TierBadge
    /// </summary>
    public static TierBadge Create(string? planType)
    {
        return new TierBadge(planType);
    }
}
