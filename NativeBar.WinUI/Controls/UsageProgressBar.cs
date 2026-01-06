using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.UI;

namespace NativeBar.WinUI.Controls;

/// <summary>
/// Custom progress bar with percentage display and color coding
/// </summary>
public sealed class UsageProgressBar : Control
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(UsageProgressBar),
            new PropertyMetadata(0.0, OnValueChanged));
    
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(
            nameof(Maximum),
            typeof(double),
            typeof(UsageProgressBar),
            new PropertyMetadata(100.0));
    
    public static readonly DependencyProperty ProviderColorProperty =
        DependencyProperty.Register(
            nameof(ProviderColor),
            typeof(Brush),
            typeof(UsageProgressBar),
            new PropertyMetadata(null));
    
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
    
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }
    
    public Brush ProviderColor
    {
        get => (Brush)GetValue(ProviderColorProperty);
        set => SetValue(ProviderColorProperty, value);
    }
    
    private Border? _fillBorder;
    private TextBlock? _percentageText;
    
    public UsageProgressBar()
    {
        DefaultStyleKey = typeof(UsageProgressBar);
    }
    
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        
        _fillBorder = GetTemplateChild("FillBorder") as Border;
        _percentageText = GetTemplateChild("PercentageText") as TextBlock;
        
        UpdateVisual();
    }
    
    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UsageProgressBar bar)
        {
            bar.UpdateVisual();
        }
    }
    
    private void UpdateVisual()
    {
        if (_fillBorder == null || _percentageText == null) return;
        
        var percentage = (Value / Maximum) * 100;
        _fillBorder.Width = ActualWidth * (Value / Maximum);
        _percentageText.Text = $"{percentage:F1}%";
        
        // Color code based on usage
        if (ProviderColor != null)
        {
            _fillBorder.Background = ProviderColor;
        }
        else if (percentage >= 90)
        {
            _fillBorder.Background = new SolidColorBrush(Colors.Red);
        }
        else if (percentage >= 70)
        {
            _fillBorder.Background = new SolidColorBrush(Colors.Orange);
        }
        else
        {
            _fillBorder.Background = new SolidColorBrush(Colors.Green);
        }
    }
}
