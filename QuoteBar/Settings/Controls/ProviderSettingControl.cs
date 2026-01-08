using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuoteBar.Core.Providers;
using QuoteBar.Core.Services;

namespace QuoteBar.Settings.Controls;

/// <summary>
/// Control for rendering dynamic provider settings
/// </summary>
public class ProviderSettingControl
{
    private readonly ThemeService _theme = ThemeService.Instance;
    private readonly SettingsService _settings = SettingsService.Instance;

    public FrameworkElement CreateControl(
        ProviderSettingDefinition definition,
        IProviderWithSettings providerSettings,
        string providerId)
    {
        var container = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(_theme.SurfaceColor),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left side: Label and description
        var labelPanel = new StackPanel { Spacing = 4 };
        labelPanel.Children.Add(new TextBlock
        {
            Text = definition.DisplayName,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = new SolidColorBrush(_theme.TextColor)
        });

        if (!string.IsNullOrEmpty(definition.Description))
        {
            labelPanel.Children.Add(new TextBlock
            {
                Text = definition.Description,
                FontSize = 12,
                Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
                TextWrapping = TextWrapping.Wrap
            });
        }

        Grid.SetColumn(labelPanel, 0);
        grid.Children.Add(labelPanel);

        // Right side: Control
        var control = CreateSettingControl(definition, providerSettings, providerId);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);

        container.Child = grid;
        return container;
    }

    private FrameworkElement CreateSettingControl(
        ProviderSettingDefinition definition,
        IProviderWithSettings providerSettings,
        string providerId)
    {
        return definition.Type switch
        {
            ProviderSettingType.Toggle => CreateToggleControl(definition, providerSettings, providerId),
            ProviderSettingType.Picker => CreatePickerControl(definition, providerSettings, providerId),
            ProviderSettingType.TextBox => CreateTextBoxControl(definition, providerSettings, providerId),
            ProviderSettingType.NumberBox => CreateNumberBoxControl(definition, providerSettings, providerId),
            ProviderSettingType.PasswordBox => CreatePasswordBoxControl(definition, providerSettings, providerId),
            _ => new TextBlock { Text = "Unknown control type" }
        };
    }

    private ToggleSwitch CreateToggleControl(
        ProviderSettingDefinition definition,
        IProviderWithSettings providerSettings,
        string providerId)
    {
        var toggle = new ToggleSwitch
        {
            OffContent = "Off",
            OnContent = "On"
        };

        _ = LoadInitialValueAsync();

        async Task LoadInitialValueAsync()
        {
            var value = await providerSettings.GetSettingValueAsync(definition.Key);
            toggle.IsOn = bool.TryParse(value, out var boolValue) && boolValue;
        }

        toggle.Toggled += async (s, e) =>
        {
            var value = toggle.IsOn ? "true" : "false";
            await SaveSettingAsync(definition, providerSettings, providerId, value);
        };

        return toggle;
    }

    private ComboBox CreatePickerControl(
        ProviderSettingDefinition definition,
        IProviderWithSettings providerSettings,
        string providerId)
    {
        var comboBox = new ComboBox
        {
            MinWidth = 150,
            PlaceholderText = "Select option"
        };

        if (definition.Options != null && definition.Options.Count > 0)
        {
            for (int i = 0; i < definition.Options.Count; i++)
            {
                var option = definition.Options[i];
                var label = definition.OptionLabels != null && i < definition.OptionLabels.Count
                    ? definition.OptionLabels[i]
                    : option;

                comboBox.Items.Add(new ComboBoxItem
                {
                    Content = label,
                    Tag = option
                });
            }
        }

        _ = LoadInitialValueAsync();

        async Task LoadInitialValueAsync()
        {
            var value = await providerSettings.GetSettingValueAsync(definition.Key);
            if (!string.IsNullOrEmpty(value))
            {
                foreach (ComboBoxItem item in comboBox.Items)
                {
                    if (item.Tag?.ToString() == value)
                    {
                        comboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        comboBox.SelectionChanged += async (s, e) =>
        {
            if (comboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                var value = item.Tag.ToString();
                await SaveSettingAsync(definition, providerSettings, providerId, value);
            }
        };

        return comboBox;
    }

    private Border CreateTextBoxControl(
        ProviderSettingDefinition definition,
        IProviderWithSettings providerSettings,
        string providerId)
    {
        var textBox = new TextBox
        {
            MinWidth = 200,
            PlaceholderText = definition.DefaultValue ?? "Enter value"
        };

        _ = LoadInitialValueAsync();

        async Task LoadInitialValueAsync()
        {
            var value = await providerSettings.GetSettingValueAsync(definition.Key);
            if (!string.IsNullOrEmpty(value))
            {
                textBox.Text = value;
            }
        }

        textBox.LostFocus += async (s, e) =>
        {
            await SaveSettingAsync(definition, providerSettings, providerId, textBox.Text);
        };

        var container = new Border
        {
            Child = textBox,
            CornerRadius = new CornerRadius(4)
        };

        return container;
    }

    private Border CreateNumberBoxControl(
        ProviderSettingDefinition definition,
        IProviderWithSettings providerSettings,
        string providerId)
    {
        var numberBox = new NumberBox
        {
            MinWidth = 120,
            Minimum = definition.MinValue ?? 0,
            Maximum = definition.MaxValue ?? 1000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };

        if (definition.Step.HasValue)
        {
            numberBox.SmallChange = definition.Step.Value;
            numberBox.LargeChange = definition.Step.Value;
        }

        _ = LoadInitialValueAsync();

        async Task LoadInitialValueAsync()
        {
            var value = await providerSettings.GetSettingValueAsync(definition.Key);
            if (!string.IsNullOrEmpty(value) && double.TryParse(value, out var numValue))
            {
                numberBox.Value = numValue;
            }
            else if (!string.IsNullOrEmpty(definition.DefaultValue) && double.TryParse(definition.DefaultValue, out var defaultNum))
            {
                numberBox.Value = defaultNum;
            }
        }

        numberBox.ValueChanged += async (s, e) =>
        {
            var value = numberBox.Value.ToString("F0");
            await SaveSettingAsync(definition, providerSettings, providerId, value);
        };

        return new Border { Child = numberBox, CornerRadius = new CornerRadius(4) };
    }

    private Border CreatePasswordBoxControl(
        ProviderSettingDefinition definition,
        IProviderWithSettings providerSettings,
        string providerId)
    {
        var passwordBox = new PasswordBox
        {
            MinWidth = 200,
            PlaceholderText = "Enter password"
        };

        _ = LoadInitialValueAsync();

        async Task LoadInitialValueAsync()
        {
            var value = await providerSettings.GetSettingValueAsync(definition.Key);
            if (!string.IsNullOrEmpty(value))
            {
                passwordBox.Password = value;
            }
        }

        passwordBox.LostFocus += async (s, e) =>
        {
            await SaveSettingAsync(definition, providerSettings, providerId, passwordBox.Password);
        };

        var container = new Border
        {
            Child = passwordBox,
            CornerRadius = new CornerRadius(4)
        };

        return container;
    }

    private async Task SaveSettingAsync(
        ProviderSettingDefinition definition,
        IProviderWithSettings providerSettings,
        string providerId,
        string? value)
    {
        if (!providerSettings.ValidateSetting(definition.Key, value, out var errorMessage))
        {
            DebugLogger.Log("ProviderSettingControl", $"Validation failed for {definition.Key}: {errorMessage}");
            return;
        }

        try
        {
            await providerSettings.ApplySettingAsync(definition.Key, value);

            var providerConfig = _settings.Settings.Providers.GetValueOrDefault(providerId) ?? new ProviderConfig();
            if (!providerConfig.Settings.TryGetValue(definition.Key, out var existingValue) || existingValue != value)
            {
                providerConfig.Settings[definition.Key] = value ?? string.Empty;
                _settings.Settings.Providers[providerId] = providerConfig;
                _settings.Save();
            }

            DebugLogger.Log("ProviderSettingControl", $"Saved {providerId}.{definition.Key} = {value}");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ProviderSettingControl", $"Failed to save {definition.Key}", ex);
        }
    }
}
