using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuoteBar.Core.Models;
using QuoteBar.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace QuoteBar.TrayPopup;

/// <summary>
/// Popup window to display request history and statistics.
/// Shows tracked API requests with provider breakdown.
/// </summary>
public sealed class RequestHistoryPopup : Window
{
    private readonly bool _isDarkMode;
    private readonly AppWindow _appWindow;
    private StackPanel _contentPanel = null!;
    private bool _isClosing;
    private DateTime _createdAt;

    public new event Action? Closed;

    // Theme colors - Windows 11 native feel, no purple
    private Windows.UI.Color BackgroundColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(250, 28, 28, 32)
        : Windows.UI.Color.FromArgb(250, 252, 252, 254);

    private Windows.UI.Color CardColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(255, 40, 40, 46)
        : Windows.UI.Color.FromArgb(255, 244, 244, 248);

    private Windows.UI.Color PrimaryTextColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
        : Windows.UI.Color.FromArgb(255, 24, 24, 28);

    private Windows.UI.Color SecondaryTextColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(255, 185, 185, 195)
        : Windows.UI.Color.FromArgb(255, 90, 90, 100);

    private Windows.UI.Color TertiaryTextColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(255, 140, 140, 150)
        : Windows.UI.Color.FromArgb(255, 120, 120, 130);

    private Windows.UI.Color DividerColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(30, 255, 255, 255)
        : Windows.UI.Color.FromArgb(20, 0, 0, 0);

    // Accent colors - Windows 11 blues and teals (no purple)
    private static Windows.UI.Color AccentBlue => Windows.UI.Color.FromArgb(255, 0, 120, 212);
    private static Windows.UI.Color AccentTeal => Windows.UI.Color.FromArgb(255, 0, 178, 148);
    private static Windows.UI.Color SuccessGreen => Windows.UI.Color.FromArgb(255, 16, 185, 129);
    private static Windows.UI.Color WarningOrange => Windows.UI.Color.FromArgb(255, 245, 158, 11);
    private static Windows.UI.Color ErrorRed => Windows.UI.Color.FromArgb(255, 239, 68, 68);

    public RequestHistoryPopup(bool isDarkMode)
    {
        _isDarkMode = isDarkMode;
        _createdAt = DateTime.UtcNow;

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // Configure as tool window
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        // Remove from taskbar
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

        // Set size
        _appWindow.Resize(new SizeInt32(340, 420));

        BuildUI();

        Activated += OnActivated;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated && !_isClosing)
        {
            var elapsed = DateTime.UtcNow - _createdAt;
            if (elapsed.TotalMilliseconds < 500) return;
            Close();
        }
    }

    private void BuildUI()
    {
        var rootGrid = new Grid { Background = new SolidColorBrush(BackgroundColor) };

        var border = new Border
        {
            Background = new SolidColorBrush(BackgroundColor),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16)
        };

        _contentPanel = new StackPanel { Spacing = 12 };
        LoadContent();

        border.Child = _contentPanel;
        rootGrid.Children.Add(border);
        Content = rootGrid;
    }

    private void LoadContent()
    {
        _contentPanel.Children.Clear();

        var tracker = RequestTracker.Instance;
        var stats = tracker.Stats;
        var history = tracker.RequestHistory;

        // Header with gradient accent bar
        var headerPanel = new Grid();
        headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        // Accent bar with gradient
        var accentBar = new Border
        {
            Width = 4,
            Height = 24,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Color = AccentBlue, Offset = 0 },
                    new GradientStop { Color = AccentTeal, Offset = 1 }
                }
            }
        };

        headerStack.Children.Add(accentBar);
        headerStack.Children.Add(new TextBlock
        {
            Text = "Request History",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(PrimaryTextColor),
            VerticalAlignment = VerticalAlignment.Center
        });

        Grid.SetColumn(headerStack, 0);
        headerPanel.Children.Add(headerStack);

        // Clear button
        var clearButton = CreateIconButton("\uE74D", "Clear History", () =>
        {
            tracker.ClearHistory();
            LoadContent();
        });
        Grid.SetColumn(clearButton, 1);
        headerPanel.Children.Add(clearButton);

        _contentPanel.Children.Add(headerPanel);

        // Stats summary cards
        var statsGrid = CreateStatsGrid(stats);
        _contentPanel.Children.Add(statsGrid);

        // Provider breakdown
        if (stats.ByProvider.Count > 0)
        {
            _contentPanel.Children.Add(CreateSectionHeader("By Provider"));
            _contentPanel.Children.Add(CreateProviderBreakdown(stats.ByProvider));
        }

        // Divider
        _contentPanel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(DividerColor),
            Margin = new Thickness(0, 4, 0, 4)
        });

        // Recent requests
        _contentPanel.Children.Add(CreateSectionHeader($"Recent Requests ({history.Count})"));

        if (history.Count == 0)
        {
            _contentPanel.Children.Add(new TextBlock
            {
                Text = "No requests tracked yet.\nAPI calls will appear here automatically.",
                FontSize = 12,
                Foreground = new SolidColorBrush(TertiaryTextColor),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20),
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 180,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var requestsList = new StackPanel { Spacing = 6 };
            foreach (var request in history.Take(15))
            {
                requestsList.Children.Add(CreateRequestCard(request));
            }

            scrollViewer.Content = requestsList;
            _contentPanel.Children.Add(scrollViewer);
        }

        // Footer
        var footerText = new TextBlock
        {
            Text = tracker.IsActive ? "Tracking active" : "Tracking paused",
            FontSize = 10,
            Foreground = new SolidColorBrush(TertiaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _contentPanel.Children.Add(footerText);
    }

    private Grid CreateStatsGrid(RequestStats stats)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Row 1: Total, Success, Failed
        AddStatCard(grid, 0, 0, stats.TotalRequests.ToString(), "Total", AccentBlue);
        AddStatCard(grid, 0, 1, stats.SuccessfulRequests.ToString(), "Success", SuccessGreen);
        AddStatCard(grid, 0, 2, stats.FailedRequests.ToString(), "Failed", ErrorRed);

        // Row 2: Rate Limited, Avg Duration, Tokens
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) }); // spacer
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddStatCard(grid, 3, 0, stats.RateLimitedRequests.ToString(), "429s", WarningOrange);
        AddStatCard(grid, 3, 1, FormatDuration(stats.AverageDurationMs), "Avg Time", AccentTeal);
        AddStatCard(grid, 3, 2, stats.TotalTokens.FormatAsTokenCount(), "Tokens", AccentBlue);

        return grid;
    }

    private void AddStatCard(Grid grid, int row, int col, string value, string label, Windows.UI.Color accentColor)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(col == 0 ? 0 : 4, 0, col == 2 ? 0 : 4, 0)
        };

        var stack = new StackPanel { Spacing = 2 };

        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(accentColor),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = new SolidColorBrush(TertiaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        card.Child = stack;
        Grid.SetRow(card, row);
        Grid.SetColumn(card, col);
        grid.Children.Add(card);
    }

    private TextBlock CreateSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(SecondaryTextColor),
            Margin = new Thickness(0, 4, 0, 4)
        };
    }

    private StackPanel CreateProviderBreakdown(Dictionary<string, ProviderRequestStats> byProvider)
    {
        var stack = new StackPanel { Spacing = 4 };

        var maxCount = byProvider.Values.Max(p => p.RequestCount);

        foreach (var (providerId, providerStats) in byProvider.OrderByDescending(p => p.Value.RequestCount))
        {
            var row = new Grid { Height = 28 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Provider name
            row.Children.Add(new TextBlock
            {
                Text = FormatProviderName(providerId),
                FontSize = 11,
                Foreground = new SolidColorBrush(PrimaryTextColor),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Progress bar
            var barContainer = new Border
            {
                Background = new SolidColorBrush(_isDarkMode
                    ? Windows.UI.Color.FromArgb(40, 255, 255, 255)
                    : Windows.UI.Color.FromArgb(30, 0, 0, 0)),
                CornerRadius = new CornerRadius(3),
                Height = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };

            var barWidth = maxCount > 0 ? (double)providerStats.RequestCount / maxCount : 0;
            var barFill = new Border
            {
                Background = new SolidColorBrush(GetProviderColor(providerId)),
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = barWidth * 100 // Will be constrained by container
            };

            barContainer.Child = barFill;
            Grid.SetColumn(barContainer, 1);
            row.Children.Add(barContainer);

            // Count
            var countText = new TextBlock
            {
                Text = providerStats.RequestCount.ToString(),
                FontSize = 11,
                Foreground = new SolidColorBrush(SecondaryTextColor),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 30,
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(countText, 2);
            row.Children.Add(countText);

            stack.Children.Add(row);
        }

        return stack;
    }

    private Border CreateRequestCard(RequestLog request)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(CardColor),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Row 1: Provider + endpoint, status badge
        var topRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        // Provider dot
        topRow.Children.Add(new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(GetProviderColor(request.Provider ?? "unknown")),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Provider name
        topRow.Children.Add(new TextBlock
        {
            Text = FormatProviderName(request.Provider),
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(PrimaryTextColor),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Endpoint (truncated)
        var endpoint = TruncateEndpoint(request.Endpoint, 25);
        topRow.Children.Add(new TextBlock
        {
            Text = endpoint,
            FontSize = 10,
            Foreground = new SolidColorBrush(TertiaryTextColor),
            VerticalAlignment = VerticalAlignment.Center
        });

        grid.Children.Add(topRow);

        // Status badge
        var statusBadge = CreateStatusBadge(request);
        Grid.SetColumn(statusBadge, 1);
        grid.Children.Add(statusBadge);

        // Row 2: Time, duration, size
        var bottomRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 4, 0, 0)
        };

        bottomRow.Children.Add(new TextBlock
        {
            Text = request.FormattedTime,
            FontSize = 9,
            Foreground = new SolidColorBrush(TertiaryTextColor)
        });

        bottomRow.Children.Add(new TextBlock
        {
            Text = request.FormattedDuration,
            FontSize = 9,
            Foreground = new SolidColorBrush(TertiaryTextColor)
        });

        if (request.ResponseSize > 0)
        {
            bottomRow.Children.Add(new TextBlock
            {
                Text = FormatBytes(request.ResponseSize),
                FontSize = 9,
                Foreground = new SolidColorBrush(TertiaryTextColor)
            });
        }

        if (request.FormattedTokens != null)
        {
            bottomRow.Children.Add(new TextBlock
            {
                Text = $"{request.FormattedTokens} tokens",
                FontSize = 9,
                Foreground = new SolidColorBrush(AccentTeal)
            });
        }

        Grid.SetRow(bottomRow, 1);
        Grid.SetColumnSpan(bottomRow, 2);
        grid.Children.Add(bottomRow);

        card.Child = grid;
        return card;
    }

    private Border CreateStatusBadge(RequestLog request)
    {
        Windows.UI.Color bgColor;
        Windows.UI.Color fgColor;

        if (request.IsSuccess)
        {
            bgColor = Windows.UI.Color.FromArgb(30, 16, 185, 129);
            fgColor = SuccessGreen;
        }
        else if (request.IsRateLimited)
        {
            bgColor = Windows.UI.Color.FromArgb(30, 245, 158, 11);
            fgColor = WarningOrange;
        }
        else
        {
            bgColor = Windows.UI.Color.FromArgb(30, 239, 68, 68);
            fgColor = ErrorRed;
        }

        return new Border
        {
            Background = new SolidColorBrush(bgColor),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock
            {
                Text = request.StatusBadge,
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(fgColor)
            }
        };
    }

    private Border CreateIconButton(string glyph, string tooltip, Action onClick)
    {
        var button = new Border
        {
            Background = new SolidColorBrush(Colors.Transparent),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6)
        };

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 12,
            Foreground = new SolidColorBrush(SecondaryTextColor)
        };

        button.Child = icon;
        ToolTipService.SetToolTip(button, tooltip);

        button.PointerEntered += (s, e) =>
        {
            button.Background = new SolidColorBrush(_isDarkMode
                ? Windows.UI.Color.FromArgb(40, 255, 255, 255)
                : Windows.UI.Color.FromArgb(30, 0, 0, 0));
        };
        button.PointerExited += (s, e) =>
        {
            button.Background = new SolidColorBrush(Colors.Transparent);
        };
        button.PointerPressed += (s, e) => onClick();

        return button;
    }

    private static Windows.UI.Color GetProviderColor(string providerId)
    {
        return providerId?.ToLower() switch
        {
            "claude" => Windows.UI.Color.FromArgb(255, 217, 119, 87),
            "openai" or "codex" => Windows.UI.Color.FromArgb(255, 16, 163, 127),
            "gemini" => Windows.UI.Color.FromArgb(255, 66, 133, 244),
            "cursor" => Windows.UI.Color.FromArgb(255, 0, 122, 255),
            "copilot" => Windows.UI.Color.FromArgb(255, 36, 41, 47),
            "augment" => Windows.UI.Color.FromArgb(255, 100, 100, 100),
            "minimax" => Windows.UI.Color.FromArgb(255, 226, 22, 126),
            _ => Windows.UI.Color.FromArgb(255, 100, 100, 110)
        };
    }

    private static string FormatProviderName(string? providerId)
    {
        if (string.IsNullOrEmpty(providerId)) return "Unknown";
        return providerId.ToLower() switch
        {
            "claude" => "Claude",
            "openai" => "OpenAI",
            "codex" => "Codex",
            "gemini" => "Gemini",
            "cursor" => "Cursor",
            "copilot" => "Copilot",
            "augment" => "Augment",
            "minimax" => "MiniMax",
            _ => char.ToUpper(providerId[0]) + providerId[1..]
        };
    }

    private static string TruncateEndpoint(string endpoint, int maxLength)
    {
        if (string.IsNullOrEmpty(endpoint)) return "";
        if (endpoint.Length <= maxLength) return endpoint;
        return "..." + endpoint[^(maxLength - 3)..];
    }

    private static string FormatDuration(int ms)
    {
        if (ms < 1000) return $"{ms}ms";
        return $"{ms / 1000.0:F1}s";
    }

    private static string FormatBytes(int bytes)
    {
        if (bytes < 1024) return $"{bytes}B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
        return $"{bytes / (1024.0 * 1024.0):F1}MB";
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
        try { base.Close(); } catch { }
        try { Closed?.Invoke(); } catch { }
    }

    // Win32 interop
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
