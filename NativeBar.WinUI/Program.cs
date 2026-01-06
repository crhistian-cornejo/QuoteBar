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
            // Write startup log
            System.IO.File.WriteAllText("D:\\NativeBar\\debug.log", 
                $"[{DateTime.Now}] Program.Main started\n");
            
            Microsoft.UI.Xaml.Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                
                System.IO.File.AppendAllText("D:\\NativeBar\\debug.log", 
                    $"[{DateTime.Now}] Creating App instance\n");
                    
                _ = new App();
                
                System.IO.File.AppendAllText("D:\\NativeBar\\debug.log", 
                    $"[{DateTime.Now}] App created successfully\n");
            });
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("D:\\NativeBar\\debug.log", 
                $"[{DateTime.Now}] PROGRAM ERROR: {ex.Message}\n{ex.StackTrace}\n");
            
            // Show message box with error
            MessageBox(IntPtr.Zero,
                $"Error al iniciar QuoteBar:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "QuoteBar Error",
                0x10); // MB_ICONERROR
        }
    }
    
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
