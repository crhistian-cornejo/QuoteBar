using Microsoft.UI.Xaml;
using NativeBar.WinUI.Core.Services;

namespace NativeBar.WinUI.TrayPopup;

/// <summary>
/// State machine for hover/click/pin popup behavior
/// </summary>
public enum PopupState
{
    Hidden,         // Not visible
    HoverPending,   // Mouse entered, waiting for delay
    HoverVisible,   // Shown due to hover
    Pinned,         // Clicked, stays visible
    ClosePending    // Mouse left, waiting to close
}

public class PopupStateManager
{
    private PopupState _state = PopupState.Hidden;
    private readonly DispatcherTimer _showDelayTimer;
    private readonly DispatcherTimer _hideDelayTimer;

    // Default delays (can be overridden by settings)
    private const int DefaultShowDelayMs = 300;
    private const int DefaultHideDelayMs = 200;

    public event Action? ShowRequested;
    public event Action? HideRequested;

    public PopupState CurrentState => _state;
    public bool IsPinned => _state == PopupState.Pinned;

    public PopupStateManager()
    {
        // Initialize with settings or defaults
        var showDelay = GetShowDelayFromSettings();
        var hideDelay = DefaultHideDelayMs;

        _showDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(showDelay) };
        _showDelayTimer.Tick += OnShowDelayElapsed;

        _hideDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(hideDelay) };
        _hideDelayTimer.Tick += OnHideDelayElapsed;

        // Subscribe to settings changes to update delays dynamically
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;

        DebugLogger.Log("PopupState", $"Initialized with showDelay={showDelay}ms, hideDelay={hideDelay}ms");
    }

    private void OnSettingsChanged()
    {
        var newShowDelay = GetShowDelayFromSettings();
        _showDelayTimer.Interval = TimeSpan.FromMilliseconds(newShowDelay);
        DebugLogger.Log("PopupState", $"Updated showDelay to {newShowDelay}ms");
    }

    private static int GetShowDelayFromSettings()
    {
        try
        {
            var delay = SettingsService.Instance.Settings.HoverDelayMs;
            // Clamp to reasonable values
            return Math.Clamp(delay, 50, 2000);
        }
        catch
        {
            return DefaultShowDelayMs;
        }
    }

    public void OnMouseEnterTrayIcon()
    {
        DebugLogger.LogDebug("PopupState", $"MouseEnterTrayIcon, current={_state}");

        switch (_state)
        {
            case PopupState.Hidden:
                _state = PopupState.HoverPending;
                _showDelayTimer.Start();
                break;

            case PopupState.ClosePending:
                _hideDelayTimer.Stop();
                _state = PopupState.HoverVisible;
                break;
        }
    }

    public void OnMouseLeaveTrayIcon()
    {
        DebugLogger.LogDebug("PopupState", $"MouseLeaveTrayIcon, current={_state}");

        switch (_state)
        {
            case PopupState.HoverPending:
                _showDelayTimer.Stop();
                _state = PopupState.Hidden;
                break;

            case PopupState.HoverVisible:
                _state = PopupState.ClosePending;
                _hideDelayTimer.Start();
                break;

            // Pinned state: do nothing
        }
    }

    public void OnMouseEnterPopup()
    {
        DebugLogger.LogDebug("PopupState", $"MouseEnterPopup, current={_state}");

        if (_state == PopupState.ClosePending)
        {
            _hideDelayTimer.Stop();
            _state = PopupState.HoverVisible;
        }
    }

    public void OnMouseLeavePopup()
    {
        DebugLogger.LogDebug("PopupState", $"MouseLeavePopup, current={_state}");

        if (_state == PopupState.HoverVisible)
        {
            _state = PopupState.ClosePending;
            _hideDelayTimer.Start();
        }
    }

    public void OnTrayIconClick()
    {
        DebugLogger.Log("PopupState", $"TrayIconClick, current={_state}");

        switch (_state)
        {
            case PopupState.Hidden:
            case PopupState.HoverPending:
                _showDelayTimer.Stop();
                _state = PopupState.Pinned;
                ShowRequested?.Invoke();
                break;

            case PopupState.HoverVisible:
            case PopupState.ClosePending:
                _hideDelayTimer.Stop();
                _state = PopupState.Pinned;
                // Already visible, just pin it
                break;

            case PopupState.Pinned:
                _state = PopupState.Hidden;
                HideRequested?.Invoke();
                break;
        }
    }

    public void OnClickOutside()
    {
        DebugLogger.LogDebug("PopupState", $"ClickOutside, current={_state}");

        if (_state == PopupState.Pinned)
        {
            _state = PopupState.Hidden;
            HideRequested?.Invoke();
        }
    }

    public void ForceHide()
    {
        _showDelayTimer.Stop();
        _hideDelayTimer.Stop();
        _state = PopupState.Hidden;
        HideRequested?.Invoke();
    }

    private void OnShowDelayElapsed(object? sender, object e)
    {
        _showDelayTimer.Stop();

        if (_state == PopupState.HoverPending)
        {
            _state = PopupState.HoverVisible;
            ShowRequested?.Invoke();
        }
    }

    private void OnHideDelayElapsed(object? sender, object e)
    {
        _hideDelayTimer.Stop();

        if (_state == PopupState.ClosePending)
        {
            _state = PopupState.Hidden;
            HideRequested?.Invoke();
        }
    }
}
