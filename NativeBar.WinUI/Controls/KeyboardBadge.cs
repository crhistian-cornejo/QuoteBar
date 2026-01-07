using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NativeBar.WinUI.Controls;

/// <summary>
/// A keyboard key badge control for displaying keyboard shortcuts.
/// Styled similar to HTML &lt;kbd&gt; elements.
/// </summary>
public sealed class KeyboardBadge : ContentControl
{
    private Border _border = null!;
    private TextBlock _textBlock = null!;

    public KeyboardBadge()
    {
        _textBlock = new TextBlock
        {
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _border = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            MinWidth = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Child = _textBlock
        };

        Content = _border;

        // Apply default theme
        ApplyTheme(IsDarkMode);
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(KeyboardBadge),
            new PropertyMetadata("", OnTextChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KeyboardBadge badge)
        {
            badge._textBlock.Text = e.NewValue as string ?? "";
        }
    }

    public static readonly DependencyProperty IsDarkModeProperty =
        DependencyProperty.Register(nameof(IsDarkMode), typeof(bool), typeof(KeyboardBadge),
            new PropertyMetadata(true, OnThemeChanged));

    public bool IsDarkMode
    {
        get => (bool)GetValue(IsDarkModeProperty);
        set => SetValue(IsDarkModeProperty, value);
    }

    private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KeyboardBadge badge)
        {
            badge.ApplyTheme((bool)e.NewValue);
        }
    }

    private void ApplyTheme(bool isDark)
    {
        if (isDark)
        {
            // Dark mode: slightly lighter background, white text
            _border.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(50, 255, 255, 255));
            _border.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 255, 255, 255));
            _border.BorderThickness = new Thickness(1);
            _textBlock.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(220, 255, 255, 255));
        }
        else
        {
            // Light mode: slightly darker background, dark text
            _border.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 0, 0, 0));
            _border.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 0, 0, 0));
            _border.BorderThickness = new Thickness(1);
            _textBlock.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 0, 0, 0));
        }
    }
}

/// <summary>
/// A panel that displays a keyboard shortcut with multiple keys.
/// Example: [Win] + [Shift] + [Q]
/// </summary>
public sealed class KeyboardShortcut : ContentControl
{
    private StackPanel _panel = null!;

    public KeyboardShortcut()
    {
        _panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        Content = _panel;
    }

    public static readonly DependencyProperty ShortcutProperty =
        DependencyProperty.Register(nameof(Shortcut), typeof(string), typeof(KeyboardShortcut),
            new PropertyMetadata("", OnShortcutChanged));

    /// <summary>
    /// The shortcut string, e.g., "Win + Shift + Q" or "Ctrl + R"
    /// </summary>
    public string Shortcut
    {
        get => (string)GetValue(ShortcutProperty);
        set => SetValue(ShortcutProperty, value);
    }

    private static void OnShortcutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KeyboardShortcut control)
        {
            control.BuildKeys(e.NewValue as string ?? "");
        }
    }

    public static readonly DependencyProperty IsDarkModeProperty =
        DependencyProperty.Register(nameof(IsDarkMode), typeof(bool), typeof(KeyboardShortcut),
            new PropertyMetadata(true, OnThemeChanged));

    public bool IsDarkMode
    {
        get => (bool)GetValue(IsDarkModeProperty);
        set => SetValue(IsDarkModeProperty, value);
    }

    private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KeyboardShortcut control)
        {
            control.BuildKeys(control.Shortcut);
        }
    }

    private void BuildKeys(string shortcut)
    {
        _panel.Children.Clear();

        if (string.IsNullOrWhiteSpace(shortcut))
            return;

        // Parse the shortcut string: "Win + Shift + Q" -> ["Win", "Shift", "Q"]
        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
        {
            // Add key badge
            var badge = new KeyboardBadge
            {
                Text = parts[i],
                IsDarkMode = IsDarkMode
            };
            _panel.Children.Add(badge);

            // Add "+" separator between keys (not after the last one)
            if (i < parts.Length - 1)
            {
                var separator = new TextBlock
                {
                    Text = "+",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(IsDarkMode
                        ? Windows.UI.Color.FromArgb(150, 255, 255, 255)
                        : Windows.UI.Color.FromArgb(150, 0, 0, 0)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 2, 0)
                };
                _panel.Children.Add(separator);
            }
        }
    }
}

/// <summary>
/// A row showing a shortcut hint: description on left, keyboard shortcut on right.
/// Example: "Toggle popup" .................. [Win] + [Shift] + [Q]
/// </summary>
public sealed class ShortcutHintRow : ContentControl
{
    private Grid _grid = null!;
    private TextBlock _descriptionText = null!;
    private KeyboardShortcut _shortcutPanel = null!;

    public ShortcutHintRow()
    {
        _grid = new Grid();
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _descriptionText = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_descriptionText, 0);
        _grid.Children.Add(_descriptionText);

        _shortcutPanel = new KeyboardShortcut();
        Grid.SetColumn(_shortcutPanel, 1);
        _grid.Children.Add(_shortcutPanel);

        Content = _grid;

        // Default theme
        ApplyTheme(IsDarkMode);
    }

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(ShortcutHintRow),
            new PropertyMetadata("", (d, e) =>
            {
                if (d is ShortcutHintRow row)
                    row._descriptionText.Text = e.NewValue as string ?? "";
            }));

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public static readonly DependencyProperty ShortcutProperty =
        DependencyProperty.Register(nameof(Shortcut), typeof(string), typeof(ShortcutHintRow),
            new PropertyMetadata("", (d, e) =>
            {
                if (d is ShortcutHintRow row)
                    row._shortcutPanel.Shortcut = e.NewValue as string ?? "";
            }));

    public string Shortcut
    {
        get => (string)GetValue(ShortcutProperty);
        set => SetValue(ShortcutProperty, value);
    }

    public static readonly DependencyProperty IsDarkModeProperty =
        DependencyProperty.Register(nameof(IsDarkMode), typeof(bool), typeof(ShortcutHintRow),
            new PropertyMetadata(true, (d, e) =>
            {
                if (d is ShortcutHintRow row)
                    row.ApplyTheme((bool)e.NewValue);
            }));

    public bool IsDarkMode
    {
        get => (bool)GetValue(IsDarkModeProperty);
        set => SetValue(IsDarkModeProperty, value);
    }

    private void ApplyTheme(bool isDark)
    {
        _descriptionText.Foreground = new SolidColorBrush(isDark
            ? Windows.UI.Color.FromArgb(180, 255, 255, 255)
            : Windows.UI.Color.FromArgb(180, 0, 0, 0));
        _shortcutPanel.IsDarkMode = isDark;
    }
}
