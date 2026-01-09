using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuoteBar.Core.CostUsage;
using QuoteBar.Core.Models;
using QuoteBar.Core.Providers;
using QuoteBar.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics;
using WinRT.Interop;

namespace QuoteBar.TrayPopup;

/// <summary>
/// Secondary popup window for cost dashboard - shows alongside main tray popup
/// </summary>
public sealed class CostDashboardPopup : Window
{
    private readonly string _providerId;
    private readonly bool _isDarkMode;
    private readonly Action? _onOpenSettings;
    private readonly AppWindow _appWindow;
    private StackPanel _contentPanel = null!;
    private bool _isClosing;
    private DateTime _createdAt;

    /// <summary>
    /// Event fired when the popup is closed (so parent can clean up reference)
    /// </summary>
    public new event Action? Closed;

    // Colors based on dark mode
    private Windows.UI.Color BackgroundColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(250, 30, 30, 35)
        : Windows.UI.Color.FromArgb(250, 251, 251, 253);

    private Windows.UI.Color PrimaryTextColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
        : Windows.UI.Color.FromArgb(255, 30, 30, 30);

    private Windows.UI.Color SecondaryTextColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(255, 200, 200, 200)
        : Windows.UI.Color.FromArgb(255, 90, 90, 90);

    private Windows.UI.Color TertiaryTextColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(255, 160, 160, 160)
        : Windows.UI.Color.FromArgb(255, 110, 110, 110);

    private Windows.UI.Color CardColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(255, 45, 45, 50)
        : Windows.UI.Color.FromArgb(255, 245, 245, 248);

    private static Windows.UI.Color AccentColor => Windows.UI.Color.FromArgb(255, 124, 58, 237);

    public CostDashboardPopup(string providerId, bool isDarkMode, Action? onOpenSettings)
    {
        _providerId = providerId;
        _isDarkMode = isDarkMode;
        _onOpenSettings = onOpenSettings;
        _createdAt = DateTime.UtcNow;

        // Get AppWindow for positioning
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // Configure as tool window (no taskbar, no activation stealing)
        var presenter = _appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        // Remove from taskbar using Win32
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

        // Set size
        _appWindow.Resize(new SizeInt32(300, 280));

        // Build UI
        BuildUI();

        // Close when clicking outside - but with grace period
        Activated += OnActivated;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated && !_isClosing)
        {
            // Grace period: don't close within first 500ms of creation
            // This prevents immediate close due to focus race conditions
            var elapsed = DateTime.UtcNow - _createdAt;
            if (elapsed.TotalMilliseconds < 500)
            {
                return;
            }

            Close();
        }
        else if (args.WindowActivationState == WindowActivationState.CodeActivated ||
                 args.WindowActivationState == WindowActivationState.PointerActivated)
        {
            // Window is now active
        }
    }

    private void BuildUI()
    {
        var rootGrid = new Grid
        {
            Background = new SolidColorBrush(BackgroundColor)
        };

        // Main border - NO visible border, matching main popup style
        var border = new Border
        {
            Background = new SolidColorBrush(BackgroundColor),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16)
        };

        _contentPanel = new StackPanel { Spacing = 12 };

        // Loading state
        _contentPanel.Children.Add(new TextBlock
        {
            Text = "Loading...",
            FontSize = 12,
            Foreground = new SolidColorBrush(SecondaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        border.Child = _contentPanel;
        rootGrid.Children.Add(border);
        Content = rootGrid;
    }

    public void PositionAt(int x, int y)
    {
        _appWindow.Move(new PointInt32(x, y));
    }

    public void Show()
    {
        Activate();
    }

    public new void Close()
    {
        if (_isClosing) return;
        _isClosing = true;

        try
        {
            base.Close();
        }
        catch { }

        // Notify parent that we've closed
        try
        {
            Closed?.Invoke();
        }
        catch { }
    }

    public async Task LoadDataAsync()
    {
        try
        {
            // Determine cost provider
            var providerIdLower = _providerId?.ToLowerInvariant();
            CostUsageProvider? costProvider = providerIdLower switch
            {
                "codex" => CostUsageProvider.Codex,
                "claude" => CostUsageProvider.Claude,
                "copilot" => CostUsageProvider.Copilot,
                _ => null
            };

            _contentPanel.Children.Clear();

            if (costProvider == null)
            {
                _contentPanel.Children.Add(new TextBlock
                {
                    Text = "Cost tracking not available\nfor this provider",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(SecondaryTextColor),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                });
                return;
            }

            // Load data
            var snapshot = await CostUsageFetcher.Instance.LoadTokenSnapshotAsync(costProvider.Value);

            // Header
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            // Provider color indicator
            var provider = !string.IsNullOrEmpty(_providerId) 
                ? ProviderRegistry.Instance.GetProvider(_providerId) 
                : null;
            var providerColor = AccentColor;
            if (provider != null && !string.IsNullOrEmpty(provider.PrimaryColor))
            {
                try
                {
                    var hex = provider.PrimaryColor.TrimStart('#');
                    providerColor = Windows.UI.Color.FromArgb(255,
                        Convert.ToByte(hex.Substring(0, 2), 16),
                        Convert.ToByte(hex.Substring(2, 2), 16),
                        Convert.ToByte(hex.Substring(4, 2), 16));
                }
                catch { }
            }

            headerPanel.Children.Add(new Border
            {
                Width = 4,
                Height = 16,
                Background = new SolidColorBrush(providerColor),
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Center
            });

            headerPanel.Children.Add(new TextBlock
            {
                Text = $"{_providerId} Cost",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(PrimaryTextColor),
                VerticalAlignment = VerticalAlignment.Center
            });

            _contentPanel.Children.Add(headerPanel);

            // Summary card
            var summaryCard = CreateSummaryCard(snapshot);
            _contentPanel.Children.Add(summaryCard);

            // Daily chart
            if (snapshot.Daily.Count > 0)
            {
                _contentPanel.Children.Add(new TextBlock
                {
                    Text = "Last 14 Days",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(SecondaryTextColor),
                    Margin = new Thickness(0, 4, 0, 0)
                });

                var chart = CreateDailyChart(snapshot.Daily.ToList(), providerColor);
                _contentPanel.Children.Add(chart);
            }
            else
            {
                _contentPanel.Children.Add(new TextBlock
                {
                    Text = "No usage data available",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(TertiaryTextColor),
                    Margin = new Thickness(0, 8, 0, 0)
                });
            }

            // Footer link
            var footerButton = new Button
            {
                Content = "Open Cost Dashboard →",
                FontSize = 10,
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = new SolidColorBrush(AccentColor),
                BorderThickness = new Thickness(0)
            };
            footerButton.Click += (s, e) =>
            {
                _onOpenSettings?.Invoke();
                Close();
            };
            _contentPanel.Children.Add(footerButton);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CostDashboardPopup", "LoadDataAsync failed", ex);
            _contentPanel.Children.Clear();
            _contentPanel.Children.Add(new TextBlock
            {
                Text = "Failed to load cost data",
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100))
            });
        }
    }

    private Border CreateSummaryCard(CostUsageTokenSnapshot snapshot)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(CardColor),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Today column
        var todayStack = new StackPanel { Spacing = 2 };
        todayStack.Children.Add(new TextBlock
        {
            Text = "Today",
            FontSize = 10,
            Foreground = new SolidColorBrush(TertiaryTextColor)
        });
        todayStack.Children.Add(new TextBlock
        {
            Text = FormatCost(snapshot.SessionCostUSD ?? 0),
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 175, 80)) // Green
        });
        todayStack.Children.Add(new TextBlock
        {
            Text = FormatTokenCount(snapshot.SessionTokens ?? 0),
            FontSize = 10,
            Foreground = new SolidColorBrush(SecondaryTextColor)
        });
        Grid.SetColumn(todayStack, 0);
        grid.Children.Add(todayStack);

        // 30-day column
        var monthStack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right };
        monthStack.Children.Add(new TextBlock
        {
            Text = "Last 30 days",
            FontSize = 10,
            Foreground = new SolidColorBrush(TertiaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        monthStack.Children.Add(new TextBlock
        {
            Text = FormatCost(snapshot.Last30DaysCostUSD ?? 0),
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(PrimaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        monthStack.Children.Add(new TextBlock
        {
            Text = FormatTokenCount(snapshot.Last30DaysTokens ?? 0),
            FontSize = 10,
            Foreground = new SolidColorBrush(SecondaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        Grid.SetColumn(monthStack, 1);
        grid.Children.Add(monthStack);

        card.Child = grid;
        return card;
    }

    private Grid CreateDailyChart(List<CostUsageDailyEntry> daily, Windows.UI.Color barColor)
    {
        var grid = new Grid { Height = 70, Margin = new Thickness(0, 4, 0, 0) };

        // Generate 14 days
        var today = DateTime.Today;
        var last14Days = Enumerable.Range(0, 14)
            .Select(i => today.AddDays(-13 + i))
            .ToList();

        // Create lookup
        var dataByDate = daily.ToDictionary(
            d => d.Date,
            d => d,
            StringComparer.OrdinalIgnoreCase);

        // Find max cost for scaling
        var maxCost = daily.Count > 0 ? daily.Max(d => d.CostUSD ?? 0) : 0;
        if (maxCost <= 0) maxCost = 1;

        var barsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        foreach (var date in last14Days)
        {
            var dateKey = CostUsageDayRange.DayKey(date);
            dataByDate.TryGetValue(dateKey, out var entry);
            var cost = entry?.CostUSD ?? 0;
            var tokens = entry?.TotalTokens ?? 0;

            var heightRatio = maxCost > 0 ? cost / maxCost : 0;
            var barHeight = cost > 0 ? Math.Max(heightRatio * 45, 3) : 2;

            var barContainer = new StackPanel { Width = 16, Spacing = 2 };

            var bar = new Border
            {
                Width = 12,
                Height = barHeight,
                Background = new SolidColorBrush(cost > 0 ? barColor :
                    Windows.UI.Color.FromArgb(40, barColor.R, barColor.G, barColor.B)),
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 45 - barHeight, 0, 0)
            };

            // Day label (sparse to avoid clutter)
            var showLabel = date.Day == 1 || date == last14Days[0] || date == last14Days[^1];
            var dayLabel = new TextBlock
            {
                Text = showLabel ? date.Day.ToString() : "",
                FontSize = 7,
                Foreground = new SolidColorBrush(TertiaryTextColor),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            barContainer.Children.Add(bar);
            barContainer.Children.Add(dayLabel);

            // Tooltip
            var tooltipText = cost > 0
                ? $"{date:MMM d}\n{FormatCost(cost)}\n{FormatTokenCount(tokens)}"
                : $"{date:MMM d}\nNo usage";
            ToolTipService.SetToolTip(barContainer, tooltipText);

            barsPanel.Children.Add(barContainer);
        }

        // Legend
        var chartStack = new StackPanel { Spacing = 2 };
        chartStack.Children.Add(barsPanel);

        var legend = new TextBlock
        {
            Text = $"Peak: {FormatCost(maxCost)}",
            FontSize = 8,
            Foreground = new SolidColorBrush(TertiaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        chartStack.Children.Add(legend);

        grid.Children.Add(chartStack);
        return grid;
    }

    private static string FormatCost(double amount)
    {
        // Use locale-aware currency formatting
        return CurrencyFormatter.FormatSmart(amount);
    }

    private static string FormatTokenCount(int tokens)
    {
        if (tokens >= 1_000_000)
            return $"{tokens / 1_000_000.0:F1}M tokens";
        if (tokens >= 1_000)
            return $"{tokens / 1_000.0:F1}K tokens";
        return $"{tokens:N0} tokens";
    }

    // Win32 interop for tool window style
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
