using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DHA.DSTC.WPF.Utilities;

namespace DHA.DSTC.WPF
{
    /// <summary>
    /// Interaction logic for App.xaml with single-instance management
    /// </summary>
    public partial class App : Application
    {
        private static Mutex _instanceMutex;
        private static NamedPipeServer _pipeServer;
        private const string MUTEX_NAME = "DHA_DSTC_WPF_SingleInstance";
        private const string PIPE_NAME = "DHA_DSTC_WPF_Pipe";

        static App()
        {// Force GDI+ rendering instead of DirectX
            
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {

                // Apply GPU-specific optimizations based on detection
                ApplyGPUOptimizations();
                System.Diagnostics.Debug.WriteLine("App.OnStartup: Starting application initialization");

                // Set up global exception handlers
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                DispatcherUnhandledException += OnDispatcherUnhandledException;
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

                // Check if another instance is already running
                bool isNewInstance;
                _instanceMutex = new Mutex(true, MUTEX_NAME, out isNewInstance);

                if (!isNewInstance)
                {
                    System.Diagnostics.Debug.WriteLine("App.OnStartup: Another instance detected, sending activation message");
                    // Another instance exists - send activation message and exit
                    SendActivationMessage();
                    Current.Shutdown();
                    return;
                }

                System.Diagnostics.Debug.WriteLine("App.OnStartup: First instance, setting up pipe server");
                // This is the first instance - set up pipe server for future activation requests
                SetupPipeServer();



                base.OnStartup(e);
                System.Diagnostics.Debug.WriteLine("App.OnStartup: Application startup completed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"App.OnStartup: Critical error during startup: {ex}");

                // Show error to user before shutting down
                MessageBox.Show(
                    $"A critical error occurred during application startup:\n\n{ex.Message}\n\nThe application will now close.",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Current.Shutdown(1);
            }
        }



        private void ApplyGPUOptimizations()
        {
            if (DetectIntelGPU())
            {
                System.Diagnostics.Debug.WriteLine("Intel GPU detected - applying compatibility fixes");

                // Apply Intel-specific rendering fixes
                System.Windows.Media.RenderOptions.ProcessRenderMode =
                    System.Windows.Interop.RenderMode.SoftwareOnly;

                // Set environment variables
                Environment.SetEnvironmentVariable("WPF_DISABLE_GPU_ACCELERATION", "1");
                Environment.SetEnvironmentVariable("INTEL_DISABLE_ALL_OPTIMIZATIONS", "1");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Non-Intel GPU detected - using hardware acceleration");

                // Keep hardware acceleration enabled for better performance
                System.Windows.Media.RenderOptions.ProcessRenderMode =
                    System.Windows.Interop.RenderMode.Default;
            }
        }


        private static bool DetectIntelGPU()
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController WHERE Name IS NOT NULL"))
                {
                    using (var collection = searcher.Get())
                    {
                        foreach (System.Management.ManagementObject obj in collection)
                        {
                            using (obj)
                            {
                                var name = obj["Name"]?.ToString();
                                if (!string.IsNullOrEmpty(name) &&
                                    name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Intel GPU detected: {name}");
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GPU detection failed - defaulting to Intel compatibility mode: {ex.Message}");
                // If we can't detect, default to Intel compatibility mode for safety
                return true;
            }

            return false;
        }

        private void SendActivationMessage()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("SendActivationMessage: Attempting to connect to existing instance");

                using (var pipeClient = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.Out))
                {
                    // Set a reasonable timeout for connection
                    pipeClient.Connect(5000); // 5 second timeout

                    using (var writer = new StreamWriter(pipeClient))
                    {
                        writer.WriteLine("ACTIVATE");
                        writer.Flush();
                    }
                }

                System.Diagnostics.Debug.WriteLine("SendActivationMessage: Activation message sent successfully");
            }
            catch (TimeoutException)
            {
                System.Diagnostics.Debug.WriteLine("SendActivationMessage: Timeout connecting to pipe, trying direct window activation");
                ActivateExistingWindow();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendActivationMessage: Failed to send activation message: {ex.Message}");
                // If pipe communication fails, try to find and activate window directly
                ActivateExistingWindow();
            }
        }

        private void ActivateExistingWindow()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ActivateExistingWindow: Searching for existing application window");

                var currentProcess = Process.GetCurrentProcess();
                var processes = Process.GetProcessesByName(currentProcess.ProcessName);

                System.Diagnostics.Debug.WriteLine($"ActivateExistingWindow: Found {processes.Length} processes with name '{currentProcess.ProcessName}'");

                foreach (var process in processes.Where(p => p.Id != currentProcess.Id))
                {
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            System.Diagnostics.Debug.WriteLine($"ActivateExistingWindow: Found window handle for process {process.Id}, attempting activation");
                            WindowHelper.ShowWindow(process.MainWindowHandle);
                            System.Diagnostics.Debug.WriteLine("ActivateExistingWindow: Window activation completed");
                            break;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"ActivateExistingWindow: Process {process.Id} has no main window handle");
                        }
                    }
                    catch (Exception processEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"ActivateExistingWindow: Error processing process {process.Id}: {processEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ActivateExistingWindow: Failed to activate existing window: {ex.Message}");

                // Last resort: show a message to the user
                MessageBox.Show(
                    "DHA Time Management is already running. Please check your system tray or taskbar.",
                    "Application Already Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void SetupPipeServer()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("SetupPipeServer: Creating named pipe server");

                _pipeServer = new NamedPipeServer(PIPE_NAME);
                _pipeServer.MessageReceived += OnActivationMessageReceived;
                _pipeServer.Start();

                System.Diagnostics.Debug.WriteLine("SetupPipeServer: Named pipe server started successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetupPipeServer: Failed to setup pipe server: {ex.Message}");
                // Application can still function without pipe server, just less elegant activation
            }
        }

        private void OnActivationMessageReceived(string message)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"OnActivationMessageReceived: Received message '{message}'");

                if (message == "ACTIVATE")
                {
                    // Use Dispatcher to ensure we're on the UI thread
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var mainWindow = Current.MainWindow as MainWindow;
                            if (mainWindow != null)
                            {
                                System.Diagnostics.Debug.WriteLine("OnActivationMessageReceived: Calling ShowApplicationFromSecondaryInstance on MainWindow");
                                mainWindow.ShowApplicationFromSecondaryInstance();
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("OnActivationMessageReceived: MainWindow is null or not of correct type");
                            }
                        }
                        catch (Exception uiEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"OnActivationMessageReceived: Error in UI thread: {uiEx.Message}");
                        }
                    }));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"OnActivationMessageReceived: Unknown message received: {message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnActivationMessageReceived: Error processing activation message: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("App.OnExit: Beginning application shutdown");

                // Clean up pipe server
                if (_pipeServer != null)
                {
                    System.Diagnostics.Debug.WriteLine("App.OnExit: Stopping pipe server");
                    _pipeServer.Stop();
                    _pipeServer.Dispose();
                    _pipeServer = null;
                }

                // Release mutex
                if (_instanceMutex != null)
                {
                    System.Diagnostics.Debug.WriteLine("App.OnExit: Releasing mutex");
                    _instanceMutex.ReleaseMutex();
                    _instanceMutex.Dispose();
                    _instanceMutex = null;
                }

                System.Diagnostics.Debug.WriteLine("App.OnExit: Cleanup completed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"App.OnExit: Error during cleanup: {ex.Message}");
                // Don't throw exceptions during shutdown
            }
            finally
            {
                base.OnExit(e);
            }
        }

        #region Global Exception Handlers

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled exception: {e.ExceptionObject}");
            Utilities.FileLogger.LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
            LogException(e.ExceptionObject as Exception, "Unhandled AppDomain Exception");
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled dispatcher exception: {e.Exception}");
            Utilities.FileLogger.LogCrash("Dispatcher.UnhandledException", e.Exception);
            LogException(e.Exception, "Unhandled Dispatcher Exception");

            // Mark as handled to prevent crash, but log the error
            e.Handled = true;

            // Show user-friendly error
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe error has been logged. The application will continue running.",
                "Unexpected Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Unobserved task exception: {e.Exception}");
            Utilities.FileLogger.LogCrash("Task.UnobservedException", e.Exception);
            LogException(e.Exception, "Unobserved Task Exception");

            // Mark as observed to prevent crash
            e.SetObserved();
        }

        private void LogException(Exception ex, string context)
        {
            if (ex == null) return;

            try
            {
                // Use your existing logging helper or create a simple file log
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}\n";
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DHA", "TimeManagement", "error.log");

                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.AppendAllText(logPath, logMessage);
            }
            catch
            {
                // Don't throw exceptions from exception handler
                System.Diagnostics.Debug.WriteLine("Failed to log exception to file");
            }
        }

        #endregion
    }
}