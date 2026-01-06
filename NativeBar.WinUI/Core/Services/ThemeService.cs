using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace NativeBar.WinUI.Core.Services;

/// <summary>
/// Theme management service for dark/light mode switching
/// </summary>
public class ThemeService
{
    private static ThemeService? _instance;
    public static ThemeService Instance => _instance ??= new ThemeService();

    private readonly UISettings _uiSettings;
    private ElementTheme _currentTheme;

    public event Action<ElementTheme>? ThemeChanged;

    public ElementTheme CurrentTheme => _currentTheme;
    public bool IsDarkMode => _currentTheme == ElementTheme.Dark;

    private ThemeService()
    {
        _uiSettings = new UISettings();
        _currentTheme = GetEffectiveTheme();

        // Listen for system theme changes
        _uiSettings.ColorValuesChanged += OnSystemThemeChanged;

        // Listen for app settings changes
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;
    }

    private void OnSystemThemeChanged(UISettings sender, object args)
    {
        var newTheme = GetEffectiveTheme();
        if (newTheme != _currentTheme)
        {
            _currentTheme = newTheme;
            ThemeChanged?.Invoke(_currentTheme);
        }
    }

    private bool _lastAccentColorSetting = true;
    private bool _lastCompactMode = false;
    private bool _lastShowProviderIcons = true;

    private void OnSettingsChanged()
    {
        var settings = SettingsService.Instance.Settings;
        var newTheme = GetEffectiveTheme();
        var themeChanged = newTheme != _currentTheme;
        
        // Check if accent color setting changed
        var accentChanged = settings.UseSystemAccentColor != _lastAccentColorSetting;
        _lastAccentColorSetting = settings.UseSystemAccentColor;
        
        // Check if appearance settings changed (these require UI rebuild)
        var appearanceChanged = settings.CompactMode != _lastCompactMode || 
                                settings.ShowProviderIcons != _lastShowProviderIcons;
        _lastCompactMode = settings.CompactMode;
        _lastShowProviderIcons = settings.ShowProviderIcons;
        
        if (themeChanged)
        {
            _currentTheme = newTheme;
        }
        
        // Trigger ThemeChanged for any visual change that requires UI rebuild
        if (themeChanged || accentChanged || appearanceChanged)
        {
            ThemeChanged?.Invoke(_currentTheme);
        }
    }

    public ElementTheme GetEffectiveTheme()
    {
        var settings = SettingsService.Instance.Settings;

        return settings.Theme switch
        {
            ThemeMode.Light => ElementTheme.Light,
            ThemeMode.Dark => ElementTheme.Dark,
            ThemeMode.System => IsSystemDarkMode() ? ElementTheme.Dark : ElementTheme.Light,
            _ => ElementTheme.Default
        };
    }

    public bool IsSystemDarkMode()
    {
        try
        {
            var foreground = _uiSettings.GetColorValue(UIColorType.Foreground);
            // Light foreground means dark mode (white text on dark background)
            return foreground.R > 128 && foreground.G > 128 && foreground.B > 128;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Apply theme to a FrameworkElement (typically Window.Content)
    /// </summary>
    public void ApplyTheme(FrameworkElement element)
    {
        if (element != null)
        {
            element.RequestedTheme = _currentTheme;
        }
    }

    /// <summary>
    /// Set theme from settings and save
    /// </summary>
    public void SetTheme(ThemeMode mode)
    {
        SettingsService.Instance.Settings.Theme = mode;
        SettingsService.Instance.Save();
    }

    // Native Windows colors for the current theme
    public Windows.UI.Color BackgroundColor => IsDarkMode
        ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
        : Windows.UI.Color.FromArgb(255, 243, 243, 243);

    public Windows.UI.Color SurfaceColor => IsDarkMode
        ? Windows.UI.Color.FromArgb(255, 44, 44, 44)
        : Windows.UI.Color.FromArgb(255, 249, 249, 249);

    public Windows.UI.Color CardColor => IsDarkMode
        ? Windows.UI.Color.FromArgb(255, 50, 50, 50)
        : Windows.UI.Color.FromArgb(255, 255, 255, 255);

    public Windows.UI.Color TextColor => IsDarkMode
        ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
        : Windows.UI.Color.FromArgb(255, 0, 0, 0);

    public Windows.UI.Color SecondaryTextColor => IsDarkMode
        ? Windows.UI.Color.FromArgb(255, 157, 157, 157)
        : Windows.UI.Color.FromArgb(255, 96, 96, 96);

    public Windows.UI.Color BorderColor => IsDarkMode
        ? Windows.UI.Color.FromArgb(255, 56, 56, 56)
        : Windows.UI.Color.FromArgb(255, 229, 229, 229);

    public Windows.UI.Color AccentColor => GetSystemAccentColor();

    private Windows.UI.Color GetSystemAccentColor()
    {
        try
        {
            if (SettingsService.Instance.Settings.UseSystemAccentColor)
            {
                return _uiSettings.GetColorValue(UIColorType.Accent);
            }
        }
        catch { }

        // Default purple accent
        return Windows.UI.Color.FromArgb(255, 124, 58, 237);
    }

    public Windows.UI.Color HoverColor => IsDarkMode
        ? Windows.UI.Color.FromArgb(255, 60, 60, 60)
        : Windows.UI.Color.FromArgb(255, 240, 240, 240);

    public Windows.UI.Color SelectedColor => IsDarkMode
        ? Windows.UI.Color.FromArgb(255, 65, 65, 65)
        : Windows.UI.Color.FromArgb(255, 230, 230, 230);

    public Windows.UI.Color SuccessColor => Windows.UI.Color.FromArgb(255, 16, 185, 129);
    public Windows.UI.Color WarningColor => Windows.UI.Color.FromArgb(255, 245, 158, 11);
    public Windows.UI.Color ErrorColor => Windows.UI.Color.FromArgb(255, 239, 68, 68);
}
