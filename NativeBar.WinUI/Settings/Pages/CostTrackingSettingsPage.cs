using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NativeBar.WinUI.Core.CostUsage;
using NativeBar.WinUI.Core.Services;
using NativeBar.WinUI.Settings.Controls;
using NativeBar.WinUI.Settings.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NativeBar.WinUI.Settings.Pages;

/// <summary>
/// Cost tracking settings page - view spending history and daily breakdown from local logs
/// Based on CodexBar's cost tracking implementation
/// </summary>
public class CostTrackingSettingsPage : ISettingsPage
{
    private readonly ThemeService _theme = ThemeService.Instance;
    private readonly CostUsageFetcher _costFetcher = CostUsageFetcher.Instance;
    private ScrollViewer? _content;
    private StackPanel? _mainStack;
    private bool _isLoading;

    // Data holders
    private CostUsageTokenSnapshot? _codexSnapshot;
    private CostUsageTokenSnapshot? _claudeSnapshot;
    private CostUsageTokenSnapshot? _copilotSnapshot;

    public FrameworkElement Content => _content ??= CreateContent();

    private ScrollViewer CreateContent()
    {
        DebugLogger.Log("CostTrackingSettingsPage", "CreateContent START");
        try
        {
            var scroll = new ScrollViewer
            {
                Padding = new Thickness(28, 20, 28, 24),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _mainStack = new StackPanel { Spacing = 16 };

            _mainStack.Children.Add(SettingCard.CreateHeader("Cost Tracking"));
            _mainStack.Children.Add(SettingCard.CreateSubheader("Local token usage and cost tracking from CLI logs"));

            // Loading indicator
            var loadingCard = CreateLoadingCard();
            _mainStack.Children.Add(loadingCard);

            scroll.Content = _mainStack;

            // Load data asynchronously
            _ = LoadCostDataAsync();

            DebugLogger.Log("CostTrackingSettingsPage", "CreateContent DONE");
            return scroll;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CostTrackingSettingsPage", "CreateContent CRASHED", ex);
            var errorScroll = new ScrollViewer { Padding = new Thickness(24) };
            var errorStack = new StackPanel();
            errorStack.Children.Add(new TextBlock
            {
                Text = $"Error loading cost tracking: {ex.Message}",
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red),
                TextWrapping = TextWrapping.Wrap
            });
            errorScroll.Content = errorStack;
            return errorScroll;
        }
    }

    private Border CreateLoadingCard()
    {
        return new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new ProgressRing { IsActive = true },
                    new TextBlock
                    {
                        Text = "Scanning local logs for cost data...",
                        FontSize = 13,
                        Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };
    }

    private async Task LoadCostDataAsync()
    {
        if (_isLoading || _mainStack == null)
            return;

        _isLoading = true;

        try
        {
            // Load cost data for supported providers
            var codexTask = _costFetcher.LoadTokenSnapshotAsync(CostUsageProvider.Codex);
            var claudeTask = _costFetcher.LoadTokenSnapshotAsync(CostUsageProvider.Claude);
            var copilotTask = _costFetcher.LoadTokenSnapshotAsync(CostUsageProvider.Copilot);

            await Task.WhenAll(codexTask, claudeTask, copilotTask);

            _codexSnapshot = codexTask.Result;
            _claudeSnapshot = claudeTask.Result;
            _copilotSnapshot = copilotTask.Result;

            // Update UI on dispatcher thread
            _mainStack.DispatcherQueue.TryEnqueue(() =>
            {
                BuildCostUI();
            });
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CostTrackingSettingsPage", "LoadCostDataAsync failed", ex);
            _mainStack.DispatcherQueue.TryEnqueue(() =>
            {
                BuildErrorUI(ex.Message);
            });
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void BuildCostUI()
    {
        if (_mainStack == null) return;

        // Remove loading indicator
        while (_mainStack.Children.Count > 2)
            _mainStack.Children.RemoveAt(2);

        // Calculate totals
        var codexTotal = _codexSnapshot?.Last30DaysCostUSD ?? 0;
        var claudeTotal = _claudeSnapshot?.Last30DaysCostUSD ?? 0;
        var copilotTotal = _copilotSnapshot?.Last30DaysCostUSD ?? 0;
        var grandTotal = codexTotal + claudeTotal + copilotTotal;

        var codexTokens = _codexSnapshot?.Last30DaysTokens ?? 0;
        var claudeTokens = _claudeSnapshot?.Last30DaysTokens ?? 0;
        var copilotRequests = _copilotSnapshot?.Last30DaysTokens ?? 0;
        var grandTotalTokens = codexTokens + claudeTokens + copilotRequests;

        // Total cost summary card
        _mainStack.Children.Add(CreateTotalCostCard(grandTotal, grandTotalTokens));

        // Provider sections
        if (_codexSnapshot != null && (_codexSnapshot.Daily.Count > 0 || _codexSnapshot.Last30DaysCostUSD > 0))
        {
            _mainStack.Children.Add(CreateProviderSection("codex", "Codex (OpenAI)", "#7C3AED", _codexSnapshot));
        }

        if (_claudeSnapshot != null && (_claudeSnapshot.Daily.Count > 0 || _claudeSnapshot.Last30DaysCostUSD > 0))
        {
            _mainStack.Children.Add(CreateProviderSection("claude", "Claude", "#D97757", _claudeSnapshot));
        }

        if (_copilotSnapshot != null && (_copilotSnapshot.Daily.Count > 0 || _copilotSnapshot.Last30DaysCostUSD > 0))
        {
            _mainStack.Children.Add(CreateProviderSection("copilot", "Copilot", "#24292F", _copilotSnapshot, isPremiumRequests: true));
        }

        // No data message if all are empty
        if (grandTotal == 0 && grandTotalTokens == 0)
        {
            _mainStack.Children.Add(CreateNoDataCard());
        }

        // Refresh button
        _mainStack.Children.Add(CreateRefreshSection());

        // Info card
        _mainStack.Children.Add(CreateInfoCard());
    }

    private Border CreateTotalCostCard(double totalCost, int totalTokens)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: Total cost
        var costStack = new StackPanel { Spacing = 4 };
        costStack.Children.Add(new TextBlock
        {
            Text = "Total Cost (30 days)",
            FontSize = 13,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor)
        });
        costStack.Children.Add(new TextBlock
        {
            Text = FormatUSD(totalCost),
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_theme.AccentColor)
        });
        Grid.SetColumn(costStack, 0);

        // Right: Total tokens
        var tokensStack = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        tokensStack.Children.Add(new TextBlock
        {
            Text = "Total Tokens",
            FontSize = 13,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        tokensStack.Children.Add(new TextBlock
        {
            Text = FormatTokenCount(totalTokens),
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_theme.TextColor),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        Grid.SetColumn(tokensStack, 1);

        grid.Children.Add(costStack);
        grid.Children.Add(tokensStack);
        card.Child = grid;

        return card;
    }

    private Border CreateProviderSection(string providerId, string providerName, string colorHex, CostUsageTokenSnapshot snapshot, bool isPremiumRequests = false)
    {
        var color = ParseColor(colorHex);

        var card = new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1)
        };

        var stack = new StackPanel { Spacing = 12 };

        // Header with provider name and totals
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        
        // Icon (branded border + SVG)
        var iconElement = ProviderIconHelper.CreateProviderImage(providerId, size: 18, forIconWithBackground: true);
        var iconBorder = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Child = iconElement ?? (FrameworkElement)ProviderIconHelper.CreateProviderInitial(providerName, 12)
        };
        
        nameStack.Children.Add(iconBorder);
        nameStack.Children.Add(new TextBlock
        {
            Text = providerName,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_theme.TextColor),
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(nameStack, 0);

        var unitLabel = isPremiumRequests ? "requests" : "tokens";
        var totalsStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        totalsStack.Children.Add(new TextBlock
        {
            Text = FormatUSD(snapshot.Last30DaysCostUSD ?? 0),
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(color)
        });
        totalsStack.Children.Add(new TextBlock
        {
            Text = $"{FormatTokenCount(snapshot.Last30DaysTokens ?? 0)} {unitLabel}",
            FontSize = 13,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(totalsStack, 1);

        header.Children.Add(nameStack);
        header.Children.Add(totalsStack);
        stack.Children.Add(header);

        // Today's usage
        if (snapshot.SessionCostUSD > 0 || snapshot.SessionTokens > 0)
        {
            var todayRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            todayRow.Children.Add(new TextBlock
            {
                Text = "Today:",
                FontSize = 12,
                Foreground = new SolidColorBrush(_theme.SecondaryTextColor)
            });
            todayRow.Children.Add(new TextBlock
            {
                Text = $"{FormatUSD(snapshot.SessionCostUSD ?? 0)} · {FormatTokenCount(snapshot.SessionTokens ?? 0)} {unitLabel}",
                FontSize = 12,
                Foreground = new SolidColorBrush(_theme.TextColor),
                HorizontalAlignment = HorizontalAlignment.Right
            });
            stack.Children.Add(todayRow);
        }

        // Daily chart
        if (snapshot.Daily.Count > 0)
        {
            stack.Children.Add(CreateDailyChart(snapshot.Daily.ToList(), color));
        }

        // Model breakdown
        var modelBreakdowns = snapshot.Daily
            .SelectMany(d => d.ModelBreakdowns ?? Enumerable.Empty<ModelBreakdown>())
            .GroupBy(m => m.ModelName)
            .Select(g => new
            {
                Model = g.Key,
                Cost = g.Sum(m => m.CostUSD ?? 0),
                Tokens = g.Sum(m => (m.InputTokens ?? 0) + (m.OutputTokens ?? 0))
            })
            .Where(m => m.Cost > 0 || m.Tokens > 0)
            .OrderByDescending(m => m.Cost)
            .Take(5)
            .ToList();

        if (modelBreakdowns.Count > 0)
        {
            var modelSection = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
            modelSection.Children.Add(new TextBlock
            {
                Text = "Top Models",
                FontSize = 11,
                Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
                Margin = new Thickness(0, 0, 0, 4)
            });

            foreach (var model in modelBreakdowns)
            {
                var displayName = isPremiumRequests 
                    ? CostUsagePricing.CopilotModelDisplayName(model.Model)
                    : CostUsagePricing.ModelDisplayName(model.Model);
                    
                var modelRow = new Grid();
                modelRow.Children.Add(new TextBlock
                {
                    Text = displayName,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(_theme.TextColor)
                });
                modelRow.Children.Add(new TextBlock
                {
                    Text = FormatUSD(model.Cost),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
                    HorizontalAlignment = HorizontalAlignment.Right
                });
                modelSection.Children.Add(modelRow);
            }

            stack.Children.Add(modelSection);
        }

        card.Child = stack;
        return card;
    }

    private Grid CreateDailyChart(List<CostUsageDailyEntry> daily, Windows.UI.Color barColor)
    {
        var grid = new Grid
        {
            Height = 120,
            Margin = new Thickness(0, 8, 0, 0)
        };

        // Generate all 14 days (including those without data)
        var today = DateTime.Today;
        var last14Days = Enumerable.Range(0, 14)
            .Select(i => today.AddDays(-13 + i))
            .ToList();

        // Create lookup for existing data
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
            Spacing = 3,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        foreach (var date in last14Days)
        {
            var dateKey = CostUsageDayRange.DayKey(date);
            var hasData = dataByDate.TryGetValue(dateKey, out var entry);
            var cost = entry?.CostUSD ?? 0;
            var tokens = entry?.TotalTokens ?? 0;

            var heightRatio = maxCost > 0 ? cost / maxCost : 0;
            var barHeight = cost > 0 ? Math.Max(heightRatio * 70, 4) : 2; // Minimum 2px line for $0 days

            var barContainer = new StackPanel { Width = 18, Spacing = 4 };

            var bar = new Border
            {
                Width = 14,
                Height = barHeight,
                Background = new SolidColorBrush(cost > 0 ? barColor : 
                    Windows.UI.Color.FromArgb(50, barColor.R, barColor.G, barColor.B)),
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 70 - barHeight, 0, 0)
            };

            // Show month for first day and when month changes
            var showMonth = date.Day == 1 || date == last14Days[0];
            var dayLabel = new TextBlock
            {
                Text = showMonth ? date.ToString("M/d") : date.Day.ToString(),
                FontSize = 8,
                Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            barContainer.Children.Add(bar);
            barContainer.Children.Add(dayLabel);

            // Tooltip with full details
            var tooltipText = cost > 0 
                ? $"{date:MMM d, yyyy}\n{FormatUSD(cost)}\n{FormatTokenCount(tokens)}"
                : $"{date:MMM d, yyyy}\nNo usage";
            ToolTipService.SetToolTip(barContainer, tooltipText);

            barsPanel.Children.Add(barContainer);
        }

        // Add legend
        var chartStack = new StackPanel { Spacing = 8 };
        chartStack.Children.Add(barsPanel);
        
        var legend = new TextBlock
        {
            Text = $"Last 14 days · Peak: {FormatUSD(maxCost)}",
            FontSize = 10,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        chartStack.Children.Add(legend);

        grid.Children.Add(chartStack);
        return grid;
    }


    private Border CreateNoDataCard()
    {
        return new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new FontIcon
                    {
                        Glyph = "\uE946",
                        FontSize = 32,
                        Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "No cost data found",
                        FontSize = 15,
                        FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                        Foreground = new SolidColorBrush(_theme.TextColor),
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "Cost tracking requires local CLI logs from Codex or Claude Code.\nMake sure you have used Codex CLI or Claude CLI at least once.",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };
    }

    private Border CreateRefreshSection()
    {
        var button = new Button
        {
            Content = "Refresh Cost Data",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 8, 16, 8)
        };
        button.Click += async (s, e) =>
        {
            button.IsEnabled = false;
            button.Content = "Refreshing...";
            try
            {
                var codexTask = _costFetcher.RefreshCostSnapshotAsync(CostUsageProvider.Codex);
                var claudeTask = _costFetcher.RefreshCostSnapshotAsync(CostUsageProvider.Claude);
                var copilotTask = _costFetcher.RefreshCostSnapshotAsync(CostUsageProvider.Copilot);

                await Task.WhenAll(codexTask, claudeTask, copilotTask);

                _codexSnapshot = codexTask.Result;
                _claudeSnapshot = claudeTask.Result;
                _copilotSnapshot = copilotTask.Result;

                BuildCostUI();
            }
            finally
            {
                button.IsEnabled = true;
                button.Content = "Refresh Cost Data";
            }
        };

        return new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Child = button
        };
    }

    private Border CreateInfoCard()
    {
        return new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 8, 0, 0),
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "Cost tracking scans local CLI logs from Codex (~/.codex/sessions) and Claude Code (~/.claude/projects), and uses GitHub's billing API for Copilot premium requests. Costs are calculated using current API pricing and may differ from your actual bill. Note: Claude Code automatically uses Haiku for some background tasks like indexing.",
                FontSize = 12,
                Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private void BuildErrorUI(string errorMessage)
    {
        if (_mainStack == null) return;

        while (_mainStack.Children.Count > 2)
            _mainStack.Children.RemoveAt(2);

        _mainStack.Children.Add(new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Red),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = $"Error loading cost data: {errorMessage}",
                FontSize = 13,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red),
                TextWrapping = TextWrapping.Wrap
            }
        });

        _mainStack.Children.Add(CreateRefreshSection());
    }

    private static string FormatUSD(double amount)
    {
        if (amount >= 1000)
            return $"${amount:N0}";
        if (amount >= 10)
            return $"${amount:F1}";
        return $"${amount:F2}";
    }

    private static string FormatTokenCount(int tokens)
    {
        if (tokens >= 1_000_000)
            return $"{tokens / 1_000_000.0:F1}M";
        if (tokens >= 1_000)
            return $"{tokens / 1_000.0:F1}K";
        return tokens.ToString("N0");
    }

    private static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return Windows.UI.Color.FromArgb(255, 128, 128, 128);

        return Windows.UI.Color.FromArgb(255,
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }

    public void OnThemeChanged()
    {
        _content = null; // Force rebuild on next access
    }
}
