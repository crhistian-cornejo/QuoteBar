using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NativeBar.WinUI.Core.Services;
using Windows.UI;

namespace NativeBar.WinUI.Controls;

/// <summary>
/// Usage history chart component showing daily usage as bar chart
/// Uses native WinUI shapes - no external dependencies
/// </summary>
public sealed class UsageHistoryChart : UserControl
{
    private Grid? _chartGrid;
    private StackPanel? _barsPanel;
    private StackPanel? _labelsPanel;
    private string? _providerId;
    private Color _providerColor = Color.FromArgb(255, 100, 100, 100);
    private int _days = 14;
    private bool _isDarkMode = true;
    private bool _isLoaded = false;

    public string? ProviderId
    {
        get => _providerId;
        set
        {
            _providerId = value;
            if (_isLoaded) UpdateChart();
        }
    }

    public Color ProviderColor
    {
        get => _providerColor;
        set
        {
            _providerColor = value;
            if (_isLoaded) UpdateChart();
        }
    }

    public int Days
    {
        get => _days;
        set
        {
            _days = value;
            if (_isLoaded) UpdateChart();
        }
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            _isDarkMode = value;
            if (_isLoaded) UpdateChart();
        }
    }

    public UsageHistoryChart()
    {
        // Defer initialization until Loaded to avoid XAML threading issues
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        try
        {
            InitializeLayout();
            _isLoaded = true;
            UpdateChart();
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("UsageHistoryChart", "OnLoaded failed", ex);
        }
    }

    private void InitializeLayout()
    {
        _chartGrid = new Grid();

        // Add row definitions using proper method (not collection initializer)
        _chartGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _chartGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Bars container - horizontal stack with bottom alignment
        _barsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Spacing = 2
        };
        Grid.SetRow(_barsPanel, 0);
        _chartGrid.Children.Add(_barsPanel);

        // Labels container
        _labelsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 4, 0, 0)
        };
        Grid.SetRow(_labelsPanel, 1);
        _chartGrid.Children.Add(_labelsPanel);

        Content = _chartGrid;
    }

    public void UpdateChart()
    {
        if (_barsPanel == null || _labelsPanel == null) return;

        _barsPanel.Children.Clear();
        _labelsPanel.Children.Clear();

        if (string.IsNullOrEmpty(_providerId))
        {
            return;
        }

        try
        {
            var history = UsageHistoryService.Instance.GetHistory(_providerId);
            var entries = history.GetLastDays(_days).ToList();

            // Build data with all days (fill gaps with 0)
            var dataPoints = new List<double>();
            var labels = new List<string>();
            var today = DateTime.UtcNow.Date;

            for (int i = _days - 1; i >= 0; i--)
            {
                var date = today.AddDays(-i);
                var entry = entries.FirstOrDefault(e => e.Date.Date == date);
                dataPoints.Add(entry?.PrimaryPercent ?? 0);

                // Show label only for first, middle, and last
                if (i == _days - 1 || i == _days / 2 || i == 0)
                    labels.Add(date.ToString("MMM d"));
                else
                    labels.Add("");
            }

            // Find max value for scaling
            var maxValue = dataPoints.Count > 0 ? dataPoints.Max() : 100;
            if (maxValue < 10) maxValue = 100; // Minimum scale

            // Calculate bar width based on available space
            var barWidth = 10;

            // Create bars
            var brush = new SolidColorBrush(_providerColor);
            var labelColor = _isDarkMode
                ? Color.FromArgb(255, 140, 140, 140)
                : Color.FromArgb(255, 100, 100, 100);
            var labelBrush = new SolidColorBrush(labelColor);

            for (int i = 0; i < dataPoints.Count; i++)
            {
                var value = dataPoints[i];
                var heightPercent = maxValue > 0 ? (value / maxValue) : 0;
                var barHeight = Math.Max(2, heightPercent * 60); // Max 60px height, min 2px

                var bar = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = brush,
                    RadiusX = 2,
                    RadiusY = 2,
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                _barsPanel.Children.Add(bar);

                // Add label
                var label = labels[i];
                var labelBlock = new TextBlock
                {
                    Text = label,
                    FontSize = 9,
                    Foreground = labelBrush,
                    Width = barWidth + 2, // Include spacing
                    TextAlignment = TextAlignment.Center
                };
                _labelsPanel.Children.Add(labelBlock);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("UsageHistoryChart", "Update failed", ex);
        }
    }

    /// <summary>
    /// Refresh chart data from history service
    /// </summary>
    public void Refresh()
    {
        if (_isLoaded) UpdateChart();
    }
}
