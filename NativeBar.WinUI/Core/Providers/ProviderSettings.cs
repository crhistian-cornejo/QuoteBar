namespace NativeBar.WinUI.Core.Providers;

/// <summary>
/// Types of provider setting controls
/// </summary>
public enum ProviderSettingType
{
    Toggle,
    Picker,
    TextBox,
    NumberBox,
    PasswordBox
}

/// <summary>
/// Definition of a provider-specific setting
/// </summary>
public class ProviderSettingDefinition
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ProviderSettingType Type { get; set; }
    public bool Required { get; set; }
    public string? DefaultValue { get; set; }

    // For Picker type
    public List<string>? Options { get; set; }
    public List<string>? OptionLabels { get; set; }

    // For NumberBox type
    public int? MinValue { get; set; }
    public int? MaxValue { get; set; }
    public int? Step { get; set; }

    /// <summary>
    /// Get display label for a picker option
    /// </summary>
    public string? GetOptionLabel(string optionValue)
    {
        if (Options == null || OptionLabels == null)
            return optionValue;

        var index = Options.IndexOf(optionValue);
        return index >= 0 && index < OptionLabels.Count
            ? OptionLabels[index]
            : optionValue;
    }
}

/// <summary>
/// Interface for providers that support dynamic settings
/// </summary>
public interface IProviderWithSettings
{
    /// <summary>
    /// Get the list of setting definitions for this provider
    /// </summary>
    List<ProviderSettingDefinition> GetSettingDefinitions();

    /// <summary>
    /// Validate a setting value
    /// </summary>
    bool ValidateSetting(string key, string? value, out string? errorMessage);

    /// <summary>
    /// Apply a setting value to the provider
    /// </summary>
    Task ApplySettingAsync(string key, string? value);

    /// <summary>
    /// Get current value for a setting
    /// </summary>
    Task<string?> GetSettingValueAsync(string key);
}

/// <summary>
/// Base implementation for provider settings
/// </summary>
public abstract class ProviderSettingsBase : IProviderWithSettings
{
    public abstract List<ProviderSettingDefinition> GetSettingDefinitions();

    public virtual bool ValidateSetting(string key, string? value, out string? errorMessage)
    {
        errorMessage = null;

        var definition = GetSettingDefinitions().FirstOrDefault(d => d.Key == key);
        if (definition == null)
        {
            errorMessage = $"Unknown setting: {key}";
            return false;
        }

        if (definition.Required && string.IsNullOrWhiteSpace(value))
        {
            errorMessage = $"This setting is required";
            return false;
        }

        if (definition.Type == ProviderSettingType.NumberBox)
        {
            if (!int.TryParse(value, out var numValue))
            {
                errorMessage = "Must be a valid number";
                return false;
            }

            if (definition.MinValue.HasValue && numValue < definition.MinValue.Value)
            {
                errorMessage = $"Must be at least {definition.MinValue.Value}";
                return false;
            }

            if (definition.MaxValue.HasValue && numValue > definition.MaxValue.Value)
            {
                errorMessage = $"Must be at most {definition.MaxValue.Value}";
                return false;
            }
        }

        return true;
    }

    public abstract Task ApplySettingAsync(string key, string? value);
    public abstract Task<string?> GetSettingValueAsync(string key);
}
