using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using NativeBar.WinUI.ViewModels;
using NativeBar.WinUI.TrayPopup;
using NativeBar.WinUI.Core.Services;
using NativeBar.WinUI.Helpers;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Runtime.InteropServices;

namespace NativeBar.WinUI;

public partial class App : Application
{
    private Window? _hiddenWindow;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private ServiceProvider? _serviceProvider;
    private NotifyIconHelper? _notifyIcon;
    private DispatcherQueue? _dispatcherQueue;
    private TrayPopupWindow? _popupWindow;
    private PopupStateManager? _popupState;
    private TaskbarOverlayHelper? _taskbarOverlay;
    private UsageStore? _usageStore;
    #pragma warning disable CS0649 // Field is never assigned - temporarily disabled, see OnLaunched
    private HotkeyService? _hotkeyService;
    #pragma warning restore CS0649
    private TrayIconsService? _trayIconsService;

    /// <summary>
    /// Gets the service provider for dependency injection
    /// </summary>
    public IServiceProvider? Services => _serviceProvider;

    /// <summary>
    /// Gets the current App instance
    /// </summary>
    public new static App? Current => Application.Current as App;

    public App()
    {
        try
        {
            // Initialize debug logger first
            DebugLogger.Initialize(enabled: true);
            DebugLogger.Log("App", "Starting");

            UnhandledException += (s, e) =>
            {
                DebugLogger.LogError("App", "CRASH", e.Exception);
                e.Handled = true;
            };

            // NOTE: XamlControlsResources removed - causes crash in unpackaged WinUI 3 apps
            // The app works fine without it; basic WinUI controls still render correctly
            InitializeComponent();

            ConfigureServices();

            // Initialize notification service
            NotificationService.Instance.Initialize();

            DebugLogger.Log("App", "Created successfully");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("App", "INIT ERROR", ex);
            throw;
        }
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Core.Services.UsageStore>();
        services.AddTransient<MainViewModel>();
        _serviceProvider = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Get dispatcher for UI thread callbacks
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            // Create hidden window
            _hiddenWindow = new Window { Title = "QuoteBar" };
            _hiddenWindow.Content = new Grid();

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_hiddenWindow);

            // Initialize popup state manager
            _popupState = new PopupStateManager();
            _popupState.ShowRequested += () => _dispatcherQueue?.TryEnqueue(OnShowPopup);
            _popupState.HideRequested += () => _dispatcherQueue?.TryEnqueue(OnHidePopup);

            // Create tray icon with dispatcher callbacks (including hover)
            _notifyIcon = new NotifyIconHelper(hwnd,
                () => _dispatcherQueue?.TryEnqueue(OnLeftClick),
                () => _dispatcherQueue?.TryEnqueue(OnRightClick),
                () => _dispatcherQueue?.TryEnqueue(OnExitClick),
                () => _dispatcherQueue?.TryEnqueue(OnHoverEnter),
                () => _dispatcherQueue?.TryEnqueue(OnHoverLeave));
            _notifyIcon.Create("QuoteBar - AI Usage Monitor");

            // Initialize taskbar overlay helper (uses hidden window handle)
            _taskbarOverlay = new TaskbarOverlayHelper(hwnd);

            // Subscribe to usage updates for tray badge
            _usageStore = _serviceProvider?.GetRequiredService<UsageStore>();
            if (_usageStore != null)
            {
                _usageStore.AllProvidersRefreshed += OnUsageDataRefreshed;
            }

            // Subscribe to settings changes (for tray badge toggle)
            SettingsService.Instance.SettingsChanged += OnSettingsChanged;

            // Initialize multiple tray icons service
            _trayIconsService = new TrayIconsService(
                _dispatcherQueue,
                () => _popupState?.OnTrayIconClick(),
                () => _notifyIcon?.ShowContextMenu(),
                OnExitClick);

            // Initialize global hotkey service - TEMPORARILY DISABLED
            // TODO: Re-enable after fixing crash
            // _hotkeyService = HotkeyService.Instance;
            // _hotkeyService.TogglePopupRequested += () => _dispatcherQueue?.TryEnqueue(OnHotkeyTogglePopup);
            // _hotkeyService.Initialize(hwnd);
            // _notifyIcon.SetHotkeyService(_hotkeyService);

            // Activate and hide
            _hiddenWindow.Activate();
            ShowWindow(hwnd, 0);

            DebugLogger.Log("App", "Ready! Icon in system tray.");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("App", "LAUNCH ERROR", ex);
        }
    }

    private void OnLeftClick()
    {
        try
        {
            DebugLogger.Log("App", "Left click - toggling popup");
            _popupState?.OnTrayIconClick();
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("App", "LEFT CLICK ERROR", ex);
        }
    }

    private void OnRightClick()
    {
        try
        {
            _notifyIcon?.ShowContextMenu();
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("App", "RIGHT CLICK ERROR", ex);
        }
    }

    private void OnExitClick()
    {
        DebugLogger.Log("App", "Exiting...");
        _hotkeyService?.Dispose();
        _trayIconsService?.Dispose();
        _taskbarOverlay?.Dispose();
        _notifyIcon?.Dispose();
        Application.Current.Exit();
    }

    private void OnHotkeyTogglePopup()
    {
        try
        {
            DebugLogger.Log("App", "Hotkey toggle popup");
            _popupState?.OnTrayIconClick();
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("App", "HOTKEY TOGGLE ERROR", ex);
        }
    }

    private void OnHoverEnter()
    {
        _popupState?.OnMouseEnterTrayIcon();
    }

    private void OnHoverLeave()
    {
        _popupState?.OnMouseLeaveTrayIcon();
    }

    private void OnUsageDataRefreshed()
    {
        _dispatcherQueue?.TryEnqueue(UpdateTrayBadge);
    }

    private void OnSettingsChanged()
    {
        _dispatcherQueue?.TryEnqueue(UpdateTrayBadge);
    }

    private void UpdateTrayBadge()
    {
        try
        {
            var settings = SettingsService.Instance.Settings;
            
            if (!settings.TrayBadgeEnabled)
            {
                // Reset to default icon if badges disabled
                _notifyIcon?.ResetToDefaultIcon();
                _trayIconsService?.RemoveAllIcons();
                return;
            }

            if (_usageStore == null || _notifyIcon == null) return;

            // Get snapshot for the selected provider
            var providerId = settings.TrayBadgeProvider ?? "claude";
            var snapshot = _usageStore.GetSnapshot(providerId);
            var isDarkMode = ThemeService.Instance.IsDarkMode;

            using var badge = TrayBadgeGenerator.GenerateBadgeFromSnapshot(
                providerId,
                snapshot,
                isDarkMode);

            _notifyIcon.UpdateIcon(badge);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("App", "UPDATE TRAY BADGE ERROR", ex);
        }
    }

    private void OnShowPopup()
    {
        try
        {
            DebugLogger.Log("App", "OnShowPopup called");

            if (_popupWindow == null)
            {
                _popupWindow = new TrayPopupWindow(_serviceProvider!);
                _popupWindow.PointerEnteredPopup += () => _dispatcherQueue?.TryEnqueue(() => _popupState?.OnMouseEnterPopup());
                _popupWindow.PointerExitedPopup += () => _dispatcherQueue?.TryEnqueue(() => _popupState?.OnMouseLeavePopup());
                _popupWindow.LightDismiss += () => _dispatcherQueue?.TryEnqueue(OnHidePopup);
                _popupWindow.SettingsRequested += () => _dispatcherQueue?.TryEnqueue(OnShowSettings);
                _popupWindow.SettingsPageRequested += (page) => _dispatcherQueue?.TryEnqueue(() => OnShowSettingsPage(page));
                _popupWindow.QuitRequested += () => _dispatcherQueue?.TryEnqueue(OnExitClick);
                _popupWindow.PinStateChanged += OnPinStateChanged;
            }

            // Get icon position and show popup
            if (_notifyIcon != null)
            {
                var (x, y, width, height) = _notifyIcon.GetIconRect();
                _popupWindow.PositionNear(x, y, width, height, taskbarAtBottom: true);
            }

            _popupWindow.ShowPopup();
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("App", "SHOW POPUP ERROR", ex);
        }
    }

    private void OnHidePopup()
    {
        try
        {
            DebugLogger.LogDebug("App", "OnHidePopup called");
            _popupWindow?.HidePopup();
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("App", "HIDE POPUP ERROR", ex);
        }
    }

    private void ShowMainWindow()
    {
        try
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow(_serviceProvider!);
                _mainWindow.Closed += (s, e) =>
                {
                    DebugLogger.Log("App", "Main window closed");
                    _mainWindow = null;
                };
            }

            _mainWindow.Activate();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);
            SetForegroundWindow(hwnd);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("App", "SHOW MAIN WINDOW ERROR", ex);
        }
    }

    private void OnShowSettings() => OnShowSettingsPage(null);

    private void OnShowSettingsPage(string? page)
    {
        try
        {
            DebugLogger.Log("App", $"Opening Settings window (page={page ?? "default"})");

            // Hide popup first
            OnHidePopup();

            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow(page);
                _settingsWindow.Closed += (s, e) =>
                {
                    DebugLogger.Log("App", "Settings window closed");
                    _settingsWindow = null;
                };
            }
            else if (page != null)
            {
                _settingsWindow.NavigateToPage(page);
            }

            _settingsWindow.Activate();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_settingsWindow);
            SetForegroundWindow(hwnd);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("App", "SHOW SETTINGS ERROR", ex);
        }
    }

    private void OnPinStateChanged(bool isPinned, string providerId, int usagePercentage, string colorHex)
    {
        try
        {
            DebugLogger.Log("App", $"PinStateChanged: isPinned={isPinned}, provider={providerId}, usage={usagePercentage}%, color={colorHex}");

            if (isPinned)
            {
                // Enable overlay and show usage
                _taskbarOverlay?.SetEnabled(true);
                var color = ParseColor(colorHex);
                _taskbarOverlay?.SetUsageOverlay(usagePercentage, color);
            }
            else
            {
                // Disable overlay
                _taskbarOverlay?.SetEnabled(false);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("App", "OnPinStateChanged ERROR", ex);
        }
    }

    private static System.Drawing.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            return System.Drawing.Color.FromArgb(
                255,
                Convert.ToInt32(hex.Substring(0, 2), 16),
                Convert.ToInt32(hex.Substring(2, 2), 16),
                Convert.ToInt32(hex.Substring(4, 2), 16)
            );
        }
        return System.Drawing.Color.FromArgb(100, 100, 100);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}

public class NotifyIconHelper : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 1;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int NIN_SELECT = WM_USER + 0;      // Primary click (left button)
    private const int NIN_POPUPOPEN = 0x0406;
    private const int NIN_POPUPCLOSE = 0x0407;
    private const int NIF_MESSAGE = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;
    private const int NIF_SHOWTIP = 0x80;
    private const int NIM_ADD = 0x00;
    private const int NIM_DELETE = 0x02;
    private const int NIM_SETVERSION = 0x04;
    private const int NOTIFYICON_VERSION_4 = 4;
    private const int TPM_RIGHTBUTTON = 0x0002;
    private const int TPM_RETURNCMD = 0x0100;
    private const int MF_STRING = 0x0000;
    private const int IDM_EXIT = 1001;

    private readonly IntPtr _hwnd;
    private readonly Action _onLeftClick;
    private readonly Action _onRightClick;
    private readonly Action _onExitClick;
    private readonly Action? _onHoverEnter;
    private readonly Action? _onHoverLeave;
    private IntPtr _iconHandle;
    private IntPtr _menuHandle;
    private bool _created;
    private WndProcDelegate? _wndProc;
    private IntPtr _oldWndProc;
    private bool _isHovering;
    private HotkeyService? _hotkeyService;
    
    private const int NIM_MODIFY = 0x01;

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public Guid guidItem;
    }

    public NotifyIconHelper(IntPtr hwnd, Action onLeftClick, Action onRightClick, Action onExitClick,
        Action? onHoverEnter = null, Action? onHoverLeave = null)
    {
        _hwnd = hwnd;
        _onLeftClick = onLeftClick;
        _onRightClick = onRightClick;
        _onExitClick = onExitClick;
        _onHoverEnter = onHoverEnter;
        _onHoverLeave = onHoverLeave;

        // Create and pin the delegate
        _wndProc = new WndProcDelegate(WndProc);
        _oldWndProc = SetWindowLongPtr(_hwnd, -4, Marshal.GetFunctionPointerForDelegate(_wndProc));

        // Create context menu
        _menuHandle = CreatePopupMenu();
        AppendMenu(_menuHandle, MF_STRING, IDM_EXIT, "Exit QuoteBar");
    }

    /// <summary>
    /// Set the hotkey service for WndProc message processing.
    /// </summary>
    public void SetHotkeyService(HotkeyService hotkeyService)
    {
        _hotkeyService = hotkeyService;
    }

    public bool Create(string tooltip)
    {
        // Load icon from LOGO-NATIVE.png
        _iconHandle = LoadLogoIcon();

        if (_iconHandle == IntPtr.Zero)
        {
            // Fallback: create programmatic icon (16x16 standard tray size)
            using var bitmap = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(124, 58, 237));
                g.FillEllipse(brush, 1, 1, 14, 14);

                using var font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold);
                using var sf = new System.Drawing.StringFormat
                {
                    Alignment = System.Drawing.StringAlignment.Center,
                    LineAlignment = System.Drawing.StringAlignment.Center
                };
                g.DrawString("N", font, System.Drawing.Brushes.White, new System.Drawing.RectangleF(0, 0, 16, 16), sf);
            }

            _iconHandle = bitmap.GetHicon();
        }

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _iconHandle,
            szTip = tooltip,
            uTimeoutOrVersion = NOTIFYICON_VERSION_4
        };

        _created = Shell_NotifyIcon(NIM_ADD, ref nid);

        if (_created)
        {
            // Set version to enable NIN_POPUPOPEN/CLOSE
            nid.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIcon(NIM_SETVERSION, ref nid);
        }

        return _created;
    }

    public (int X, int Y, int Width, int Height) GetIconRect()
    {
        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _hwnd,
            uID = 1
        };

        if (Shell_NotifyIconGetRect(ref id, out RECT rect) == 0) // S_OK
        {
            return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        // Fallback: use cursor position
        GetCursorPos(out POINT pt);
        return (pt.X - 16, pt.Y - 32, 32, 32);
    }

    /// <summary>
    /// Update the tray icon with a new bitmap (for dynamic usage badges)
    /// </summary>
    public void UpdateIcon(System.Drawing.Bitmap bitmap)
    {
        if (!_created) return;

        try
        {
            // Create new icon handle
            var newIconHandle = bitmap.GetHicon();

            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_ICON,
                hIcon = newIconHandle
            };

            if (Shell_NotifyIcon(NIM_MODIFY, ref nid))
            {
                // Destroy old icon handle
                if (_iconHandle != IntPtr.Zero)
                    DestroyIcon(_iconHandle);
                _iconHandle = newIconHandle;
            }
            else
            {
                // Failed to update, destroy the new handle
                DestroyIcon(newIconHandle);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("NotifyIcon", "UpdateIcon ERROR", ex);
        }
    }

    /// <summary>
    /// Reset to the default logo icon
    /// </summary>
    public void ResetToDefaultIcon()
    {
        if (!_created) return;

        try
        {
            var newIconHandle = LoadLogoIcon();
            if (newIconHandle == IntPtr.Zero) return;

            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_ICON,
                hIcon = newIconHandle
            };

            if (Shell_NotifyIcon(NIM_MODIFY, ref nid))
            {
                if (_iconHandle != IntPtr.Zero)
                    DestroyIcon(_iconHandle);
                _iconHandle = newIconHandle;
            }
            else
            {
                DestroyIcon(newIconHandle);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("NotifyIcon", "ResetToDefaultIcon ERROR", ex);
        }
    }

    public void ShowContextMenu()
    {
        GetCursorPos(out POINT pt);
        SetForegroundWindow(_hwnd);
        int cmd = TrackPopupMenu(_menuHandle, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        if (cmd == IDM_EXIT) _onExitClick?.Invoke();
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            // Process hotkey messages first
            if (_hotkeyService?.ProcessMessage(msg, wParam, lParam) == true)
            {
                return IntPtr.Zero;
            }

            if (msg == WM_TRAYICON)
            {
                // For NOTIFYICON_VERSION_4:
                // LOWORD(lParam) = notification event (NIN_SELECT, WM_LBUTTONUP, etc.)
                // HIWORD(lParam) = icon ID
                // GET_X_LPARAM(wParam) = x coord
                // GET_Y_LPARAM(wParam) = y coord
                int mouseMsg = (int)(lParam.ToInt64() & 0xFFFF);

                switch (mouseMsg)
                {
                    case WM_LBUTTONUP:
                    case NIN_SELECT:  // Primary click notification
                        _onLeftClick?.Invoke();
                        return IntPtr.Zero;

                    case WM_RBUTTONUP:
                    case WM_CONTEXTMENU:  // Context menu request
                        _onRightClick?.Invoke();
                        return IntPtr.Zero;

                    case NIN_POPUPOPEN:
                        if (!_isHovering)
                        {
                            _isHovering = true;
                            _onHoverEnter?.Invoke();
                        }
                        return IntPtr.Zero;

                    case NIN_POPUPCLOSE:
                        if (_isHovering)
                        {
                            _isHovering = false;
                            _onHoverLeave?.Invoke();
                        }
                        return IntPtr.Zero;

                    case WM_MOUSEMOVE:
                        // Fallback hover detection via mouse tracking
                        if (!_isHovering && _onHoverEnter != null)
                        {
                            _isHovering = true;
                            _onHoverEnter?.Invoke();
                            StartMouseLeaveTracking();
                        }
                        return IntPtr.Zero;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("WndProc", "Error processing tray message", ex);
        }

        return CallWindowProc(_oldWndProc, hwnd, msg, wParam, lParam);
    }

    private System.Threading.Timer? _mouseLeaveTimer;

    private IntPtr LoadLogoIcon()
    {
        try
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var exeDir = System.IO.Path.GetDirectoryName(exePath) ?? "";
            var logoPath = System.IO.Path.Combine(exeDir, "Assets", "LOGO-NATIVE.png");

            if (!System.IO.File.Exists(logoPath))
            {
                // Try alternate paths
                var appDir = AppContext.BaseDirectory;
                logoPath = System.IO.Path.Combine(appDir, "Assets", "LOGO-NATIVE.png");
            }

            if (System.IO.File.Exists(logoPath))
            {
                using var originalBitmap = new System.Drawing.Bitmap(logoPath);
                // Use 16x16 for standard system tray icon size
                using var resizedBitmap = new System.Drawing.Bitmap(16, 16);
                using (var g = System.Drawing.Graphics.FromImage(resizedBitmap))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.DrawImage(originalBitmap, 0, 0, 16, 16);
                }
                return resizedBitmap.GetHicon();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("NotifyIcon", "LoadLogoIcon ERROR", ex);
        }
        return IntPtr.Zero;
    }

    private void StartMouseLeaveTracking()
    {
        _mouseLeaveTimer?.Dispose();
        _mouseLeaveTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                GetCursorPos(out POINT pt);
                var (x, y, w, h) = GetIconRect();

                bool isOverIcon = pt.X >= x && pt.X <= x + w && pt.Y >= y && pt.Y <= y + h;

                if (!isOverIcon && _isHovering)
                {
                    _isHovering = false;
                    _onHoverLeave?.Invoke();
                    _mouseLeaveTimer?.Dispose();
                    _mouseLeaveTimer = null;
                }
            }
            catch { }
        }, null, 100, 100);
    }

    public void Dispose()
    {
        _mouseLeaveTimer?.Dispose();

        if (_created)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
        }

        if (_menuHandle != IntPtr.Zero) DestroyMenu(_menuHandle);
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, int uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, int uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
