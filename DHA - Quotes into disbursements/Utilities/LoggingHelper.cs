using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace DHA.DSTC.WPF.Utilities
{
    /// <summary>
    /// File-based debug logger. When enabled, captures ALL System.Diagnostics.Debug output
    /// plus explicit log calls to a daily rolling log file.
    /// Toggle on/off at runtime via Enable() / Disable().
    /// Log files: %LOCALAPPDATA%\DHA\TimeManagement\Logs\debug_yyyy-MM-dd.log
    /// </summary>
    public static class FileLogger
    {
        private static readonly object _lock = new object();
        private static StreamWriter _writer;
        private static FileLogTraceListener _traceListener;
        private static string _currentLogPath;
        private static DateTime _currentLogDate;
        private static bool _isEnabled;

        /// <summary>Whether file logging is currently active.</summary>
        public static bool IsEnabled => _isEnabled;

        /// <summary>Full path to the current (or most recent) log file.</summary>
        public static string CurrentLogPath => _currentLogPath;

        /// <summary>Directory where log files are stored.</summary>
        public static string LogDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DHA", "TimeManagement", "Logs");

        /// <summary>
        /// Enable file logging. Attaches a TraceListener to System.Diagnostics.Debug
        /// so all existing Debug.WriteLine calls are automatically captured.
        /// </summary>
        public static void Enable()
        {
            lock (_lock)
            {
                if (_isEnabled) return;

                try
                {
                    // Ensure directory exists
                    if (!Directory.Exists(LogDirectory))
                        Directory.CreateDirectory(LogDirectory);

                    // Open today's log file (append mode)
                    OpenLogFile();

                    // Attach trace listener to capture Debug.WriteLine output
                    _traceListener = new FileLogTraceListener();
                    Debug.Listeners.Add(_traceListener);

                    _isEnabled = true;

                    // Write startup header
                    WriteRaw("════════════════════════════════════════════════════════════════");
                    WriteRaw($"  DHA Time Management — Debug Log Started");
                    WriteRaw($"  {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    WriteRaw($"  Machine: {Environment.MachineName}");
                    WriteRaw($"  Windows User: {Environment.UserName}");
                    WriteRaw($"  OS: {Environment.OSVersion}");
                    WriteRaw($"  App Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
                    WriteRaw($"  CLR: {Environment.Version}");
                    WriteRaw($"  64-bit Process: {Environment.Is64BitProcess}");
                    WriteRaw("════════════════════════════════════════════════════════════════");
                    WriteRaw("");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"FileLogger: Failed to enable: {ex.Message}");
                    _isEnabled = false;
                }
            }
        }

        /// <summary>Disable file logging and flush/close the file.</summary>
        public static void Disable()
        {
            lock (_lock)
            {
                if (!_isEnabled) return;

                try
                {
                    WriteRaw("");
                    WriteRaw("════════════════════════════════════════════════════════════════");
                    WriteRaw($"  Debug Log Ended — {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    WriteRaw("════════════════════════════════════════════════════════════════");

                    if (_traceListener != null)
                    {
                        Debug.Listeners.Remove(_traceListener);
                        _traceListener = null;
                    }

                    _writer?.Flush();
                    _writer?.Dispose();
                    _writer = null;
                }
                catch { }

                _isEnabled = false;
            }
        }

        /// <summary>Write an INFO-level message to the log file.</summary>
        public static void Info(string message)
        {
            WriteEntry("INFO", message);
        }

        /// <summary>Write a WARNING-level message to the log file.</summary>
        public static void Warn(string message)
        {
            WriteEntry("WARN", message);
        }

        /// <summary>Write an ERROR-level message to the log file.</summary>
        public static void Error(string message, Exception ex = null)
        {
            WriteEntry("ERROR", message);
            if (ex != null)
            {
                WriteEntry("ERROR", $"  Exception: {ex.GetType().FullName}: {ex.Message}");
                WriteEntry("ERROR", $"  Stack: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    WriteEntry("ERROR", $"  Inner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                    WriteEntry("ERROR", $"  Inner Stack: {ex.InnerException.StackTrace}");
                }
            }
        }

        /// <summary>Log a detailed snapshot of the current application state.</summary>
        public static void LogAppState()
        {
            if (!_isEnabled) return;

            WriteRaw("");
            WriteRaw("── APPLICATION STATE SNAPSHOT ──────────────────────────────────");
            WriteEntry("STATE", $"ServiceLocator.IsInitialized: {ServiceLocator.IsInitialized}");
            WriteEntry("STATE", $"ServiceLocator.CurrentUserId: {ServiceLocator.CurrentUserId}");
            WriteEntry("STATE", $"ServiceLocator.CurrentUserName: {ServiceLocator.CurrentUserName}");
            WriteEntry("STATE", $"ServiceLocator.CurrentUserEmail: {ServiceLocator.CurrentUserEmail}");
            WriteEntry("STATE", $"ServiceLocator.IsImpersonating: {ServiceLocator.IsImpersonating}");
            WriteEntry("STATE", $"ServiceLocator.IsConnectionValid: {ServiceLocator.IsConnectionValid()}");

            try
            {
                var authService = Services.DataverseAuthService.Instance;
                WriteEntry("STATE", $"AuthService.IsConnected: {authService?.IsConnected}");
                WriteEntry("STATE", $"CrmClient.IsReady: {authService?.Client?.IsReady}");
                WriteEntry("STATE", $"CrmClient.CallerId: {authService?.Client?.CallerId}");
                WriteEntry("STATE", $"CrmClient.ConnectedOrgVersion: {authService?.Client?.ConnectedOrgVersion}");

                var orgService = authService?.OrganizationService;
                WriteEntry("STATE", $"OrganizationService type: {orgService?.GetType().FullName ?? "null"}");
            }
            catch (Exception ex)
            {
                WriteEntry("STATE", $"Error getting auth state: {ex.Message}");
            }

            try
            {
                WriteEntry("STATE", $"Working set: {Environment.WorkingSet / 1024 / 1024} MB");
                WriteEntry("STATE", $"Thread count: {Process.GetCurrentProcess().Threads.Count}");
            }
            catch { }

            WriteRaw("───────────────────────────────────────────────────────────────");
            WriteRaw("");
        }

        /// <summary>
        /// Log an unhandled exception (called from global exception handlers).
        /// Always writes even if debug mode is off — enables itself temporarily.
        /// </summary>
        public static void LogCrash(string context, Exception ex)
        {
            bool wasEnabled = _isEnabled;
            if (!wasEnabled)
            {
                try { Enable(); } catch { return; }
            }

            WriteRaw("");
            WriteRaw("╔══════════════════════════════════════════════════════════════╗");
            WriteRaw($"║  UNHANDLED EXCEPTION — {context,-37} ║");
            WriteRaw("╚══════════════════════════════════════════════════════════════╝");
            Error($"Unhandled exception in {context}", ex);
            LogAppState();

            if (!wasEnabled)
            {
                try { Disable(); } catch { }
            }
        }

        /// <summary>Delete log files older than the specified number of days.</summary>
        public static void CleanupOldLogs(int keepDays = 14)
        {
            try
            {
                if (!Directory.Exists(LogDirectory)) return;

                var cutoff = DateTime.Today.AddDays(-keepDays);
                foreach (var file in Directory.GetFiles(LogDirectory, "debug_*.log"))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        try { File.Delete(file); }
                        catch { /* best effort */ }
                    }
                }
            }
            catch { }
        }

        #region Internals

        private static void OpenLogFile()
        {
            var today = DateTime.Today;
            _currentLogDate = today;
            _currentLogPath = Path.Combine(LogDirectory, $"debug_{today:yyyy-MM-dd}.log");
            _writer = new StreamWriter(_currentLogPath, append: true, encoding: Encoding.UTF8)
            {
                AutoFlush = true
            };
        }

        private static void RollIfNeeded()
        {
            if (DateTime.Today != _currentLogDate)
            {
                _writer?.Flush();
                _writer?.Dispose();
                OpenLogFile();
            }
        }

        private static void WriteEntry(string level, string message)
        {
            if (!_isEnabled || _writer == null) return;

            lock (_lock)
            {
                try
                {
                    RollIfNeeded();
                    _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level,-5}] [{Thread.CurrentThread.ManagedThreadId,3}] {message}");
                }
                catch { /* don't throw from logger */ }
            }
        }

        private static void WriteRaw(string line)
        {
            if (!_isEnabled || _writer == null) return;

            lock (_lock)
            {
                try
                {
                    RollIfNeeded();
                    _writer.WriteLine(line);
                }
                catch { }
            }
        }

        /// <summary>
        /// Called by the TraceListener to write Debug.WriteLine output to the log.
        /// </summary>
        internal static void WriteTrace(string message)
        {
            if (!_isEnabled || _writer == null) return;

            // Avoid empty/whitespace-only lines
            if (string.IsNullOrWhiteSpace(message)) return;

            lock (_lock)
            {
                try
                {
                    RollIfNeeded();
                    _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [TRACE] [{Thread.CurrentThread.ManagedThreadId,3}] {message}");
                }
                catch { }
            }
        }

        #endregion
    }

    /// <summary>
    /// TraceListener that forwards all Debug.WriteLine calls to FileLogger.
    /// </summary>
    internal class FileLogTraceListener : TraceListener
    {
        public override void Write(string message)
        {
            // Buffer partial writes — we only log full lines
        }

        public override void WriteLine(string message)
        {
            FileLogger.WriteTrace(message);
        }
    }
}
