using System;
using System.Drawing;
using System.Runtime.InteropServices;
using NativeBar.WinUI.Core.Services;

namespace NativeBar.WinUI.Helpers;

/// <summary>
/// Helper class for setting taskbar overlay icons using ITaskbarList3
/// </summary>
public class TaskbarOverlayHelper : IDisposable
{
    private readonly IntPtr _hwnd;
    private ITaskbarList3? _taskbarList;
    private IntPtr _currentOverlayIcon;
    private bool _isEnabled;

    // COM CLSID and IID for ITaskbarList3
    private static readonly Guid CLSID_TaskbarList = new("56FDF344-FD6D-11d0-958A-006097C9A090");
    private static readonly Guid IID_ITaskbarList3 = new("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF");

    public TaskbarOverlayHelper(IntPtr hwnd)
    {
        _hwnd = hwnd;
        InitializeTaskbarList();
    }

    private void InitializeTaskbarList()
    {
        try
        {
            var taskbarListType = Type.GetTypeFromCLSID(CLSID_TaskbarList);
            if (taskbarListType != null)
            {
                var obj = Activator.CreateInstance(taskbarListType);
                _taskbarList = obj as ITaskbarList3;
                _taskbarList?.HrInit();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TaskbarOverlayHelper", "Init error", ex);
        }
    }

    /// <summary>
    /// Enable or disable the overlay icon
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        _isEnabled = enabled;
        if (!enabled)
        {
            ClearOverlay();
        }
    }

    /// <summary>
    /// Set overlay icon showing usage percentage
    /// </summary>
    /// <param name="percentage">Usage percentage (0-100)</param>
    /// <param name="providerColor">Provider color for the overlay</param>
    public void SetUsageOverlay(int percentage, Color providerColor)
    {
        if (!_isEnabled || _taskbarList == null) return;

        try
        {
            // Clean up previous icon
            if (_currentOverlayIcon != IntPtr.Zero)
            {
                DestroyIcon(_currentOverlayIcon);
                _currentOverlayIcon = IntPtr.Zero;
            }

            // Create overlay icon
            _currentOverlayIcon = CreateUsageIcon(percentage, providerColor);

            if (_currentOverlayIcon != IntPtr.Zero)
            {
                string description = $"{percentage}% usage";
                _taskbarList.SetOverlayIcon(_hwnd, _currentOverlayIcon, description);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TaskbarOverlayHelper", "SetUsageOverlay error", ex);
        }
    }

    /// <summary>
    /// Set overlay icon showing status indicator
    /// </summary>
    public void SetStatusOverlay(UsageStatus status, Color providerColor)
    {
        if (!_isEnabled || _taskbarList == null) return;

        try
        {
            if (_currentOverlayIcon != IntPtr.Zero)
            {
                DestroyIcon(_currentOverlayIcon);
                _currentOverlayIcon = IntPtr.Zero;
            }

            _currentOverlayIcon = CreateStatusIcon(status, providerColor);

            if (_currentOverlayIcon != IntPtr.Zero)
            {
                string description = status switch
                {
                    UsageStatus.Normal => "Normal usage",
                    UsageStatus.Warning => "High usage",
                    UsageStatus.Critical => "Critical usage",
                    _ => "Unknown"
                };
                _taskbarList.SetOverlayIcon(_hwnd, _currentOverlayIcon, description);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TaskbarOverlayHelper", "SetStatusOverlay error", ex);
        }
    }

    /// <summary>
    /// Clear the overlay icon
    /// </summary>
    public void ClearOverlay()
    {
        try
        {
            if (_currentOverlayIcon != IntPtr.Zero)
            {
                DestroyIcon(_currentOverlayIcon);
                _currentOverlayIcon = IntPtr.Zero;
            }

            _taskbarList?.SetOverlayIcon(_hwnd, IntPtr.Zero, null);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TaskbarOverlayHelper", "ClearOverlay error", ex);
        }
    }

    private IntPtr CreateUsageIcon(int percentage, Color providerColor)
    {
        try
        {
            // Create a 16x16 icon (standard overlay size)
            using var bitmap = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bitmap);

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            // Determine color based on percentage
            Color fillColor;
            if (percentage >= 90)
                fillColor = Color.FromArgb(220, 38, 38); // Red - critical
            else if (percentage >= 70)
                fillColor = Color.FromArgb(245, 158, 11); // Orange - warning
            else
                fillColor = providerColor.A > 0 ? providerColor : Color.FromArgb(34, 197, 94); // Green or provider color

            // Draw circular background
            using var bgBrush = new SolidBrush(fillColor);
            g.FillEllipse(bgBrush, 0, 0, 15, 15);

            // Draw border
            using var borderPen = new Pen(Color.White, 1);
            g.DrawEllipse(borderPen, 0, 0, 15, 15);

            // Draw percentage text
            string text = percentage >= 100 ? "!" : percentage.ToString();
            using var font = new Font("Segoe UI", percentage >= 100 ? 9 : (percentage >= 10 ? 6 : 7), FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.White);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            g.DrawString(text, font, textBrush, new RectangleF(0, 0, 16, 16), sf);

            return bitmap.GetHicon();
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private IntPtr CreateStatusIcon(UsageStatus status, Color providerColor)
    {
        try
        {
            using var bitmap = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bitmap);

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            Color fillColor = status switch
            {
                UsageStatus.Critical => Color.FromArgb(220, 38, 38),
                UsageStatus.Warning => Color.FromArgb(245, 158, 11),
                _ => providerColor.A > 0 ? providerColor : Color.FromArgb(34, 197, 94)
            };

            // Draw filled circle
            using var bgBrush = new SolidBrush(fillColor);
            g.FillEllipse(bgBrush, 1, 1, 14, 14);

            // Draw border
            using var borderPen = new Pen(Color.White, 1);
            g.DrawEllipse(borderPen, 1, 1, 14, 14);

            // Draw icon based on status
            using var iconBrush = new SolidBrush(Color.White);
            if (status == UsageStatus.Critical)
            {
                // Exclamation mark
                using var font = new Font("Segoe UI", 10, FontStyle.Bold);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("!", font, iconBrush, new RectangleF(0, 0, 16, 16), sf);
            }
            else if (status == UsageStatus.Warning)
            {
                // Warning triangle outline
                using var font = new Font("Segoe UI", 8, FontStyle.Bold);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("!", font, iconBrush, new RectangleF(0, 0, 16, 16), sf);
            }
            else
            {
                // Checkmark
                using var pen = new Pen(Color.White, 2);
                g.DrawLine(pen, 4, 8, 7, 11);
                g.DrawLine(pen, 7, 11, 12, 5);
            }

            return bitmap.GetHicon();
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        ClearOverlay();

        if (_taskbarList != null)
        {
            Marshal.ReleaseComObject(_taskbarList);
            _taskbarList = null;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// ITaskbarList3 COM interface
    /// </summary>
    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, TBPFLAG tbpFlags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint dwReserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string? pszDescription);
        void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
        void SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
    }

    private enum TBPFLAG
    {
        TBPF_NOPROGRESS = 0,
        TBPF_INDETERMINATE = 0x1,
        TBPF_NORMAL = 0x2,
        TBPF_ERROR = 0x4,
        TBPF_PAUSED = 0x8
    }
}

public enum UsageStatus
{
    Normal,
    Warning,
    Critical
}
