using System.Globalization;

namespace QuoteBar.Core.Services;

/// <summary>
/// Locale-aware currency formatting service.
/// Supports system locale detection and manual currency symbol override.
/// </summary>
public static class CurrencyFormatter
{
    /// <summary>
    /// Currency display mode
    /// </summary>
    public enum CurrencyMode
    {
        /// <summary>Use system locale for currency symbol and formatting</summary>
        System,
        /// <summary>Always display as USD ($)</summary>
        USD,
        /// <summary>Always display as EUR (€)</summary>
        EUR,
        /// <summary>Always display as GBP (£)</summary>
        GBP,
        /// <summary>Always display as JPY (¥)</summary>
        JPY,
        /// <summary>Always display as CNY (¥)</summary>
        CNY,
        /// <summary>Always display as PEN (S/.) - Peruvian Sol</summary>
        PEN
    }

    private static readonly Dictionary<CurrencyMode, (string Symbol, string Code, int Decimals, double ExchangeRate)> CurrencyInfo = new()
    {
        { CurrencyMode.USD, ("$", "USD", 2, 1.0) },
        { CurrencyMode.EUR, ("€", "EUR", 2, 0.92) },
        { CurrencyMode.GBP, ("£", "GBP", 2, 0.79) },
        { CurrencyMode.JPY, ("¥", "JPY", 0, 157.0) },
        { CurrencyMode.CNY, ("¥", "CNY", 2, 7.30) },
        { CurrencyMode.PEN, ("S/.", "PEN", 2, 3.75) }
    };

    /// <summary>
    /// Get the current currency mode from settings
    /// </summary>
    public static CurrencyMode CurrentMode =>
        Enum.TryParse<CurrencyMode>(SettingsService.Instance.Settings.CurrencyDisplayMode, out var mode)
            ? mode
            : CurrencyMode.System;

    /// <summary>
    /// Format a cost value using the configured currency mode.
    /// Values are assumed to be in USD from providers - no conversion is performed.
    /// </summary>
    /// <param name="amount">Amount in USD from provider</param>
    /// <param name="includeCode">Whether to include currency code (e.g., "USD")</param>
    /// <returns>Formatted currency string</returns>
    public static string Format(double amount, bool includeCode = false)
    {
        var mode = CurrentMode;

        if (mode == CurrencyMode.System)
        {
            return FormatWithSystemLocale(amount, includeCode);
        }

        return FormatWithMode(amount, mode, includeCode);
    }

    /// <summary>
    /// Format cost with smart precision (fewer decimals for large amounts)
    /// </summary>
    public static string FormatSmart(double amount, bool includeCode = false)
    {
        var mode = CurrentMode;
        var (symbol, code, defaultDecimals, exchangeRate) = GetCurrencyInfo(mode);

        // Apply exchange rate conversion
        var convertedAmount = amount * exchangeRate;

        // Determine precision based on converted amount
        int decimals;
        if (convertedAmount >= 1000)
            decimals = 0;
        else if (convertedAmount >= 10)
            decimals = 1;
        else
            decimals = defaultDecimals;

        var formatted = FormatAmount(convertedAmount, symbol, decimals);

        if (includeCode)
            return $"{formatted} {code}";

        return formatted;
    }

    /// <summary>
    /// Format with explicit currency mode override
    /// </summary>
    public static string FormatWithMode(double amount, CurrencyMode mode, bool includeCode = false)
    {
        var (symbol, code, decimals, exchangeRate) = GetCurrencyInfo(mode);

        // Apply exchange rate conversion
        var convertedAmount = amount * exchangeRate;
        var formatted = FormatAmount(convertedAmount, symbol, decimals);

        if (includeCode)
            return $"{formatted} {code}";

        return formatted;
    }

    /// <summary>
    /// Format using system locale
    /// </summary>
    private static string FormatWithSystemLocale(double amount, bool includeCode)
    {
        try
        {
            var culture = CultureInfo.CurrentCulture;
            var regionInfo = new RegionInfo(culture.Name);

            // Format with system currency
            var formatted = amount.ToString("C", culture);

            if (includeCode)
            {
                // Append ISO currency code
                return $"{formatted} {regionInfo.ISOCurrencySymbol}";
            }

            return formatted;
        }
        catch
        {
            // Fall back to USD if locale detection fails
            return FormatWithMode(amount, CurrencyMode.USD, includeCode);
        }
    }

    private static (string Symbol, string Code, int Decimals, double ExchangeRate) GetCurrencyInfo(CurrencyMode mode)
    {
        if (mode == CurrencyMode.System)
        {
            try
            {
                var culture = CultureInfo.CurrentCulture;
                var regionInfo = new RegionInfo(culture.Name);
                var nfi = culture.NumberFormat;

                // JPY and other zero-decimal currencies
                var decimals = nfi.CurrencyDecimalDigits;

                return (nfi.CurrencySymbol, regionInfo.ISOCurrencySymbol, decimals, 1.0);
            }
            catch
            {
                return CurrencyInfo[CurrencyMode.USD];
            }
        }

        return CurrencyInfo.TryGetValue(mode, out var info) ? info : CurrencyInfo[CurrencyMode.USD];
    }

    private static string FormatAmount(double amount, string symbol, int decimals)
    {
        var format = decimals switch
        {
            0 => "N0",
            1 => "N1",
            _ => "N2"
        };

        return $"{symbol}{amount.ToString(format)}";
    }

    /// <summary>
    /// Get display name for a currency mode (for settings UI)
    /// </summary>
    public static string GetDisplayName(CurrencyMode mode)
    {
        return mode switch
        {
            CurrencyMode.System => "System Default",
            CurrencyMode.USD => "US Dollar ($)",
            CurrencyMode.EUR => "Euro (€)",
            CurrencyMode.GBP => "British Pound (£)",
            CurrencyMode.JPY => "Japanese Yen (¥)",
            CurrencyMode.CNY => "Chinese Yuan (¥)",
            CurrencyMode.PEN => "Peruvian Sol (S/.)",
            _ => mode.ToString()
        };
    }

    /// <summary>
    /// Get all available currency modes
    /// </summary>
    public static IEnumerable<CurrencyMode> GetAllModes()
    {
        return Enum.GetValues<CurrencyMode>();
    }
}
