using Microsoft.UI.Xaml;

namespace QuoteBar.Settings;

/// <summary>
/// Interface for settings pages
/// </summary>
public interface ISettingsPage
{
    /// <summary>
    /// The root UI element of this page
    /// </summary>
    FrameworkElement Content { get; }

    /// <summary>
    /// Called when the page is navigated to
    /// </summary>
    void OnNavigatedTo() { }

    /// <summary>
    /// Called when the page is navigated away from
    /// </summary>
    void OnNavigatedFrom() { }

    /// <summary>
    /// Called when the theme changes
    /// </summary>
    void OnThemeChanged() { }
}
