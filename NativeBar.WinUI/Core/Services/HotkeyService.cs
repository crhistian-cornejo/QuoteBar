using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace NativeBar.WinUI.Core.Services;

/// <summary>
/// Manages global keyboard hotkeys for QuoteBar.
/// Uses Win32 RegisterHotKey API for system-wide hotkey registration.
/// 
/// Default hotkey: Win+Shift+Q (safe, doesn't conflict with Windows shortcuts)
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private static HotkeyService? _instance;
    public static HotkeyService Instance => _instance ??= new HotkeyService();

    // Hotkey identifiers
    private const int HOTKEY_TOGGLE_POPUP = 1;
    private const int HOTKEY_REFRESH = 2;

    // Win32 constants
    private const int WM_HOTKEY = 0x0312;

    // Modifier keys
    [Flags]
    public enum ModifierKeys : uint
    {
        None = 0x0000,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
        NoRepeat = 0x4000  // Prevents repeated WM_HOTKEY messages when key is held
    }

    // Virtual key codes for common keys
    public static class VirtualKeys
    {
        public const uint A = 0x41;
        public const uint B = 0x42;
        public const uint C = 0x43;
        public const uint D = 0x44;
        public const uint E = 0x45;
        public const uint F = 0x46;
        public const uint G = 0x47;
        public const uint H = 0x48;
        public const uint I = 0x49;
        public const uint J = 0x4A;
        public const uint K = 0x4B;
        public const uint L = 0x4C;
        public const uint M = 0x4D;
        public const uint N = 0x4E;
        public const uint O = 0x4F;
        public const uint P = 0x50;
        public const uint Q = 0x51;
        public const uint R = 0x52;
        public const uint S = 0x53;
        public const uint T = 0x54;
        public const uint U = 0x55;
        public const uint V = 0x56;
        public const uint W = 0x57;
        public const uint X = 0x58;
        public const uint Y = 0x59;
        public const uint Z = 0x5A;
        public const uint Backtick = 0xC0;  // ` key
        public const uint Semicolon = 0xBA; // ; key
        public const uint Space = 0x20;
    }

    // Current registration state
    private IntPtr _hwnd;
    private bool _isRegistered;
    private HotkeyBinding _currentBinding;
    private bool _bindingLoaded;

    // Events
    public event Action? TogglePopupRequested;
    public event Action? RefreshRequested;

    private HotkeyService()
    {
        // Initialize with default binding first - DON'T access SettingsService here
        // to avoid circular initialization issues
        _currentBinding = new HotkeyBinding
        {
            Key = VirtualKeys.Q,
            Win = true,
            Shift = true,
            DisplayString = "Win + Shift + Q"
        };
    }

    /// <summary>
    /// Initialize the hotkey service with a window handle for message processing.
    /// Must be called before hotkeys can be registered.
    /// </summary>
    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        
        try
        {
            // Load binding from settings (deferred from constructor to avoid init issues)
            if (!_bindingLoaded)
            {
                try
                {
                    _currentBinding = LoadBindingFromSettings();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("HotkeyService", "LoadBindingFromSettings failed", ex);
                }
                _bindingLoaded = true;
            }

            if (SettingsService.Instance.Settings.HotkeyEnabled)
            {
                Register();
            }
            
            DebugLogger.Log("HotkeyService", $"Initialized with hwnd={hwnd}, enabled={SettingsService.Instance.Settings.HotkeyEnabled}");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("HotkeyService", "Initialize failed", ex);
        }
    }

    /// <summary>
    /// Register the global hotkey with Windows.
    /// </summary>
    public bool Register()
    {
        if (_hwnd == IntPtr.Zero)
        {
            DebugLogger.Log("HotkeyService", "Cannot register: no hwnd");
            return false;
        }

        // Unregister first if already registered
        if (_isRegistered)
        {
            Unregister();
        }

        var modifiers = _currentBinding.GetModifiersValue() | (uint)ModifierKeys.NoRepeat;
        var result = RegisterHotKey(_hwnd, HOTKEY_TOGGLE_POPUP, modifiers, _currentBinding.Key);

        if (result)
        {
            _isRegistered = true;
            DebugLogger.Log("HotkeyService", $"Registered hotkey: {_currentBinding.DisplayString}");
        }
        else
        {
            var error = Marshal.GetLastWin32Error();
            DebugLogger.Log("HotkeyService", $"Failed to register hotkey: error {error} (hotkey may be in use by another app)");
        }

        return result;
    }

    /// <summary>
    /// Unregister the global hotkey.
    /// </summary>
    public void Unregister()
    {
        if (_isRegistered && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_TOGGLE_POPUP);
            _isRegistered = false;
            DebugLogger.Log("HotkeyService", "Unregistered hotkey");
        }
    }

    /// <summary>
    /// Process Windows messages. Call this from your WndProc.
    /// Returns true if the message was handled.
    /// </summary>
    public bool ProcessMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();
            
            switch (hotkeyId)
            {
                case HOTKEY_TOGGLE_POPUP:
                    DebugLogger.Log("HotkeyService", "Toggle popup hotkey pressed");
                    TogglePopupRequested?.Invoke();
                    return true;
                    
                case HOTKEY_REFRESH:
                    DebugLogger.Log("HotkeyService", "Refresh hotkey pressed");
                    RefreshRequested?.Invoke();
                    return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// Update the hotkey binding and re-register.
    /// </summary>
    public bool SetBinding(HotkeyBinding binding)
    {
        _currentBinding = binding;
        SaveBindingToSettings(binding);
        
        if (SettingsService.Instance.Settings.HotkeyEnabled && _hwnd != IntPtr.Zero)
        {
            return Register();
        }
        
        return true;
    }

    /// <summary>
    /// Enable or disable the global hotkey.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        var settings = SettingsService.Instance.Settings;
        settings.HotkeyEnabled = enabled;
        SettingsService.Instance.Save();

        if (enabled)
        {
            Register();
        }
        else
        {
            Unregister();
        }
    }

    /// <summary>
    /// Get the current hotkey binding.
    /// </summary>
    public HotkeyBinding CurrentBinding => _currentBinding;

    /// <summary>
    /// Check if hotkey is currently registered.
    /// </summary>
    public bool IsRegistered => _isRegistered;

    /// <summary>
    /// Get a list of safe default hotkey options that don't conflict with Windows.
    /// </summary>
    public static IReadOnlyList<HotkeyBinding> GetSafeDefaults()
    {
        return new List<HotkeyBinding>
        {
            // Primary recommendation: Win+Shift+Q
            new HotkeyBinding
            {
                Key = VirtualKeys.Q,
                Win = true,
                Shift = true,
                DisplayString = "Win + Shift + Q",
                Description = "Recommended - Q for QuoteBar"
            },
            // Alternative: Win+Alt+Q
            new HotkeyBinding
            {
                Key = VirtualKeys.Q,
                Win = true,
                Alt = true,
                DisplayString = "Win + Alt + Q",
                Description = "Alternative"
            },
            // Alternative: Ctrl+Alt+Q
            new HotkeyBinding
            {
                Key = VirtualKeys.Q,
                Control = true,
                Alt = true,
                DisplayString = "Ctrl + Alt + Q",
                Description = "Classic style"
            },
            // Alternative: Win+Shift+U
            new HotkeyBinding
            {
                Key = VirtualKeys.U,
                Win = true,
                Shift = true,
                DisplayString = "Win + Shift + U",
                Description = "U for Usage"
            },
            // Alternative: Win+` (backtick)
            new HotkeyBinding
            {
                Key = VirtualKeys.Backtick,
                Win = true,
                DisplayString = "Win + `",
                Description = "Quick access"
            }
        };
    }

    private HotkeyBinding LoadBindingFromSettings()
    {
        var settings = SettingsService.Instance.Settings;
        
        if (!string.IsNullOrEmpty(settings.HotkeyKey))
        {
            return new HotkeyBinding
            {
                Key = settings.HotkeyKey switch
                {
                    "Q" => VirtualKeys.Q,
                    "U" => VirtualKeys.U,
                    "`" => VirtualKeys.Backtick,
                    _ => VirtualKeys.Q
                },
                Win = settings.HotkeyModifiers.Contains("Win"),
                Shift = settings.HotkeyModifiers.Contains("Shift"),
                Alt = settings.HotkeyModifiers.Contains("Alt"),
                Control = settings.HotkeyModifiers.Contains("Ctrl"),
                DisplayString = settings.HotkeyDisplayString ?? "Win + Shift + Q"
            };
        }
        
        // Default: Win+Shift+Q
        return GetSafeDefaults()[0];
    }

    private void SaveBindingToSettings(HotkeyBinding binding)
    {
        var settings = SettingsService.Instance.Settings;
        
        // Convert key code to string
        settings.HotkeyKey = binding.Key switch
        {
            VirtualKeys.Q => "Q",
            VirtualKeys.U => "U",
            VirtualKeys.Backtick => "`",
            _ => "Q"
        };
        
        // Build modifiers list
        var modifiers = new List<string>();
        if (binding.Win) modifiers.Add("Win");
        if (binding.Shift) modifiers.Add("Shift");
        if (binding.Alt) modifiers.Add("Alt");
        if (binding.Control) modifiers.Add("Ctrl");
        settings.HotkeyModifiers = modifiers;
        
        settings.HotkeyDisplayString = binding.DisplayString;
        
        SettingsService.Instance.Save();
    }

    public void Dispose()
    {
        Unregister();
    }

    #region P/Invoke

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    #endregion
}

/// <summary>
/// Represents a hotkey binding (modifier keys + main key).
/// </summary>
public class HotkeyBinding
{
    public uint Key { get; set; }
    public bool Win { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public bool Control { get; set; }
    
    [JsonIgnore]
    public string DisplayString { get; set; } = "";
    
    [JsonIgnore]
    public string? Description { get; set; }

    public uint GetModifiersValue()
    {
        uint result = 0;
        if (Alt) result |= (uint)HotkeyService.ModifierKeys.Alt;
        if (Control) result |= (uint)HotkeyService.ModifierKeys.Control;
        if (Shift) result |= (uint)HotkeyService.ModifierKeys.Shift;
        if (Win) result |= (uint)HotkeyService.ModifierKeys.Win;
        return result;
    }

    /// <summary>
    /// Get the display string for the key portion (for kbd element).
    /// </summary>
    public string GetKeyDisplayString()
    {
        return Key switch
        {
            HotkeyService.VirtualKeys.Q => "Q",
            HotkeyService.VirtualKeys.U => "U",
            HotkeyService.VirtualKeys.Backtick => "`",
            HotkeyService.VirtualKeys.Semicolon => ";",
            _ => "?"
        };
    }

    /// <summary>
    /// Get individual modifier display strings (for kbd elements).
    /// </summary>
    public IEnumerable<string> GetModifierDisplayStrings()
    {
        if (Win) yield return "Win";
        if (Ctrl) yield return "Ctrl";
        if (Alt) yield return "Alt";
        if (Shift) yield return "Shift";
    }

    // Alias for Control to match common naming
    [JsonIgnore]
    public bool Ctrl
    {
        get => Control;
        set => Control = value;
    }
}
