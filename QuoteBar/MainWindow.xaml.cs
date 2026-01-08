using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuoteBar.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using QuoteBar.Core.Models;
using QuoteBar.Core.Services;

namespace QuoteBar;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow(ServiceProvider serviceProvider)
    {
        InitializeComponent();
        ViewModel = serviceProvider.GetRequiredService<MainViewModel>();

        // Set window size
        AppWindow.Resize(new Windows.Graphics.SizeInt32(400, 600));

        // Load initial data
        _ = ViewModel.RefreshAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
    }
    
    private void OnProviderSwitchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Content is string providerId)
        {
            ViewModel.SwitchProvider(providerId);
        }
    }
    
    // Helper methods for XAML binding
    public string FormatPercentage(double percentage)
    {
        return $"{percentage:F1}%";
    }
    
    public string FormatUsage(RateWindow? window)
    {
        if (window == null) return string.Empty;
        
        if (window.Used.HasValue && window.Limit.HasValue)
        {
            // Show decimals if the value has decimal places
            var used = window.Used.Value;
            var limit = window.Limit.Value;
            var unit = window.Unit ?? "";

            string usedStr = used % 1 == 0 ? $"{used:N0}" : $"{used:N2}";
            string limitStr = limit % 1 == 0 ? $"{limit:N0}" : $"{limit:N0}";

            return $"{usedStr} / {limitStr} {unit}".Trim();
        }

        if (window.Used.HasValue)
        {
            var used = window.Used.Value;
            var unit = window.Unit ?? "";
            string usedStr = used % 1 == 0 ? $"{used:N0}" : $"{used:N2}";
            return $"{usedStr} {unit}".Trim();
        }
        
        return $"{window.UsedPercent:F1}%";
    }
    
    public string FormatCost(double cost)
    {
        // Use locale-aware currency formatting
        return CurrencyFormatter.Format(cost, includeCode: true);
    }
}
