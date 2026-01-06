using Microsoft.UI.Xaml;

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

    public event Action? ShowRequested;
    public event Action? HideRequested;

    public PopupState CurrentState => _state;
    public bool IsPinned => _state == PopupState.Pinned;

    public PopupStateManager()
    {
        _showDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _showDelayTimer.Tick += OnShowDelayElapsed;

        _hideDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _hideDelayTimer.Tick += OnHideDelayElapsed;
    }

    public void OnMouseEnterTrayIcon()
    {
        System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
            $"[{DateTime.Now}] PopupState: MouseEnterTrayIcon, current={_state}\n");

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
        System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
            $"[{DateTime.Now}] PopupState: MouseLeaveTrayIcon, current={_state}\n");

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
        System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
            $"[{DateTime.Now}] PopupState: MouseEnterPopup, current={_state}\n");

        if (_state == PopupState.ClosePending)
        {
            _hideDelayTimer.Stop();
            _state = PopupState.HoverVisible;
        }
    }

    public void OnMouseLeavePopup()
    {
        System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
            $"[{DateTime.Now}] PopupState: MouseLeavePopup, current={_state}\n");

        if (_state == PopupState.HoverVisible)
        {
            _state = PopupState.ClosePending;
            _hideDelayTimer.Start();
        }
    }

    public void OnTrayIconClick()
    {
        System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
            $"[{DateTime.Now}] PopupState: TrayIconClick, current={_state}\n");

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
        System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
            $"[{DateTime.Now}] PopupState: ClickOutside, current={_state}\n");

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
