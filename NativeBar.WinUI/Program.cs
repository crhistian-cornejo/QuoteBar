using Microsoft.UI.Dispatching;
using System;
using System.Runtime.InteropServices;

namespace NativeBar.WinUI;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        try
        {
            Microsoft.UI.Xaml.Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);

                _ = new App();
            });
        }
        catch (Exception ex)
        {
            // Show message box with error - DebugLogger may not be initialized yet
            MessageBox(IntPtr.Zero,
                $"Error starting QuoteBar:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "QuoteBar Error",
                0x10); // MB_ICONERROR
        }
    }
    
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
