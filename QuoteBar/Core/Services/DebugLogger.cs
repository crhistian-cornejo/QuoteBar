using System.Diagnostics;
using System.IO;

namespace QuoteBar.Core.Services;

/// <summary>
/// Centralized debug logging service for QuoteBar.
/// Uses buffered writes to minimize disk I/O impact.
/// Only logs in DEBUG builds or when explicitly enabled.
/// </summary>
public static class DebugLogger
{
    private static readonly object _lock = new();
    private static string? _logFilePath;
    private static bool _isEnabled;
    private static StreamWriter? _logWriter;
    private static Timer? _flushTimer;
    private const int FlushIntervalMs = 5000; // Flush every 5 seconds

    /// <summary>
    /// Initialize the logger with optional custom path
    /// </summary>
    public static void Initialize(bool enabled = false, string? customLogPath = null)
    {
        _isEnabled = enabled || IsDebugBuild();

        if (_isEnabled)
        {
            _logFilePath = customLogPath ?? GetDefaultLogPath();

            // Ensure directory exists
            var dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch { }
            }

            // Open buffered stream writer
            try
            {
                _logWriter = new StreamWriter(_logFilePath, append: true)
                {
                    AutoFlush = false // Manual flush for performance
                };

                // Periodic flush timer
                _flushTimer = new Timer(_ => Flush(), null, FlushIntervalMs, FlushIntervalMs);
            }
            catch { }

            Log("DebugLogger", "Logger initialized");
        }
    }

    /// <summary>
    /// Check if running in debug build
    /// </summary>
    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    /// <summary>
    /// Get default log file path in AppData
    /// </summary>
    private static string GetDefaultLogPath()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuoteBar",
            "logs");

        return Path.Combine(appDataPath, $"debug_{DateTime.Now:yyyy-MM-dd}.log");
    }

    /// <summary>
    /// Log a message with category (buffered write)
    /// </summary>
    public static void Log(string category, string message)
    {
        if (!_isEnabled)
            return;

        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logLine = $"[{timestamp}] [{category}] {message}";

            lock (_lock)
            {
                if (_logWriter != null)
                {
                    _logWriter.WriteLine(logLine);
                }
                else if (!string.IsNullOrEmpty(_logFilePath))
                {
                    // Fallback to direct write if stream not available
                    File.AppendAllText(_logFilePath, logLine + "\n");
                }
            }

            // Also output to debugger
            Debug.WriteLine($"[{category}] {message}");
        }
        catch
        {
            // Silently ignore logging errors
        }
    }

    /// <summary>
    /// Log an error with exception details
    /// </summary>
    public static void LogError(string category, string message, Exception? ex = null)
    {
        var fullMessage = ex != null
            ? $"ERROR: {message} - {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"
            : $"ERROR: {message}";

        Log(category, fullMessage);

        // Immediately flush errors to disk
        Flush();
    }

    /// <summary>
    /// Log only in debug builds (no-op in release)
    /// </summary>
    [Conditional("DEBUG")]
    public static void LogDebug(string category, string message)
    {
        Log(category, $"[DEBUG] {message}");
    }

    /// <summary>
    /// Flush buffered logs to disk
    /// </summary>
    public static void Flush()
    {
        lock (_lock)
        {
            try
            {
                _logWriter?.Flush();
            }
            catch { }
        }
    }

    /// <summary>
    /// Enable or disable logging at runtime
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        _isEnabled = enabled;
        if (enabled && string.IsNullOrEmpty(_logFilePath))
        {
            Initialize(enabled);
        }
    }

    /// <summary>
    /// Get current log file path
    /// </summary>
    public static string? GetLogFilePath() => _logFilePath;

    /// <summary>
    /// Shutdown the logger (call on app exit)
    /// </summary>
    public static void Shutdown()
    {
        lock (_lock)
        {
            _flushTimer?.Dispose();
            _flushTimer = null;

            try
            {
                _logWriter?.Flush();
                _logWriter?.Dispose();
            }
            catch { }
            _logWriter = null;
        }
    }

    /// <summary>
    /// Clean up old log files (keep last N days)
    /// </summary>
    public static void CleanupOldLogs(int keepDays = 7)
    {
        if (string.IsNullOrEmpty(_logFilePath))
            return;

        try
        {
            var logDir = Path.GetDirectoryName(_logFilePath);
            if (string.IsNullOrEmpty(logDir) || !Directory.Exists(logDir))
                return;

            var cutoffDate = DateTime.Now.AddDays(-keepDays);
            var logFiles = Directory.GetFiles(logDir, "debug_*.log");

            foreach (var file in logFiles)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTime < cutoffDate)
                {
                    try
                    {
                        fileInfo.Delete();
                    }
                    catch { }
                }
            }
        }
        catch { }
    }
}
