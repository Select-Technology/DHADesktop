using System;
using System.Runtime.InteropServices;

namespace DHA.DSTC.WPF.Utilities
{
    /// <summary>
    /// Helper class for Win32 window manipulation operations
    /// </summary>
    public static class WindowHelper
    {
        #region Win32 API Declarations

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlashWindow(IntPtr hWnd, bool bInvert);

        #endregion

        #region Constants

        private const int SW_HIDE = 0;
        private const int SW_SHOWNORMAL = 1;
        private const int SW_SHOWMINIMIZED = 2;
        private const int SW_SHOWMAXIMIZED = 3;
        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_SHOW = 5;
        private const int SW_MINIMIZE = 6;
        private const int SW_SHOWMINNOACTIVE = 7;
        private const int SW_SHOWNA = 8;
        private const int SW_RESTORE = 9;
        private const int SW_SHOWDEFAULT = 10;
        private const int SW_FORCEMINIMIZE = 11;

        private const uint SPI_GETFOREGROUNDLOCKTIMEOUT = 0x2000;
        private const uint SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;
        private const uint SPIF_SENDCHANGE = 0x02;

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;

            public static WINDOWPLACEMENT Default
            {
                get
                {
                    var result = new WINDOWPLACEMENT();
                    result.length = Marshal.SizeOf(result);
                    return result;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;

            public RECT(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }

        #endregion

        /// <summary>
        /// Shows and activates a window using its handle
        /// </summary>
        /// <param name="windowHandle">Handle to the window to show</param>
        public static void ShowWindow(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("WindowHelper.ShowWindow: Invalid window handle (IntPtr.Zero)");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"WindowHelper.ShowWindow: Attempting to show window with handle 0x{windowHandle.ToInt64():X}");

                // Get current window state
                bool isMinimized = IsIconic(windowHandle);
                bool isVisible = IsWindowVisible(windowHandle);

                System.Diagnostics.Debug.WriteLine($"WindowHelper.ShowWindow: Window state - Minimized: {isMinimized}, Visible: {isVisible}");

                // First, ensure the window is visible and restored
                if (isMinimized)
                {
                    System.Diagnostics.Debug.WriteLine("WindowHelper.ShowWindow: Restoring minimized window");
                    ShowWindow(windowHandle, SW_RESTORE);
                }
                else if (!isVisible)
                {
                    System.Diagnostics.Debug.WriteLine("WindowHelper.ShowWindow: Showing hidden window");
                    ShowWindow(windowHandle, SW_SHOW);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("WindowHelper.ShowWindow: Window already visible, bringing to front");
                    ShowWindow(windowHandle, SW_SHOWNORMAL);
                }

                // Bring the window to the foreground
                ForceWindowToForeground(windowHandle);

                // Flash the window to draw attention
                FlashWindow(windowHandle, false);

                System.Diagnostics.Debug.WriteLine("WindowHelper.ShowWindow: Window activation completed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WindowHelper.ShowWindow: Failed to show window: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"WindowHelper.ShowWindow: Exception details: {ex}");
            }
        }

        /// <summary>
        /// Forces a window to come to the foreground using multiple techniques
        /// </summary>
        /// <param name="windowHandle">Handle to the window</param>
        private static void ForceWindowToForeground(IntPtr windowHandle)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("WindowHelper.ForceWindowToForeground: Starting foreground activation");

                // Get the current foreground window
                IntPtr foregroundWindow = GetForegroundWindow();

                if (foregroundWindow == windowHandle)
                {
                    System.Diagnostics.Debug.WriteLine("WindowHelper.ForceWindowToForeground: Window is already in foreground");
                    return;
                }

                // Try the simple approach first
                if (SetForegroundWindow(windowHandle))
                {
                    System.Diagnostics.Debug.WriteLine("WindowHelper.ForceWindowToForeground: Simple SetForegroundWindow succeeded");
                    return;
                }

                // If simple approach failed, try advanced technique
                System.Diagnostics.Debug.WriteLine("WindowHelper.ForceWindowToForeground: Simple approach failed, trying advanced technique");

                // Get thread IDs
                uint currentThreadId = GetCurrentThreadId();
                uint foregroundThreadId = 0;
                uint targetThreadId = 0;

                if (foregroundWindow != IntPtr.Zero)
                {
                    foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out uint _);
                }

                if (windowHandle != IntPtr.Zero)
                {
                    targetThreadId = GetWindowThreadProcessId(windowHandle, out uint _);
                }

                System.Diagnostics.Debug.WriteLine($"WindowHelper.ForceWindowToForeground: Thread IDs - Current: {currentThreadId}, Foreground: {foregroundThreadId}, Target: {targetThreadId}");

                // Try to temporarily disable the foreground lock timeout
                uint originalLockTimeout = 0;
                bool timeoutChanged = false;

                try
                {
                    if (SystemParametersInfo(SPI_GETFOREGROUNDLOCKTIMEOUT, 0, ref originalLockTimeout, 0))
                    {
                        uint newTimeout = 0;
                        if (SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, ref newTimeout, SPIF_SENDCHANGE))
                        {
                            timeoutChanged = true;
                            System.Diagnostics.Debug.WriteLine("WindowHelper.ForceWindowToForeground: Temporarily disabled foreground lock timeout");
                        }
                    }
                }
                catch (Exception timeoutEx)
                {
                    System.Diagnostics.Debug.WriteLine($"WindowHelper.ForceWindowToForeground: Could not change timeout: {timeoutEx.Message}");
                }

                try
                {
                    // Attach input threads if they're different and valid
                    bool inputAttached = false;
                    if (foregroundThreadId != 0 && currentThreadId != 0 &&
                        foregroundThreadId != currentThreadId &&
                        foregroundThreadId != targetThreadId)
                    {
                        inputAttached = AttachThreadInput(currentThreadId, foregroundThreadId, true);
                        System.Diagnostics.Debug.WriteLine($"WindowHelper.ForceWindowToForeground: Input thread attachment result: {inputAttached}");
                    }

                    try
                    {
                        // Multiple activation attempts
                        BringWindowToTop(windowHandle);
                        SetForegroundWindow(windowHandle);
                        ShowWindow(windowHandle, SW_SHOW);

                        System.Diagnostics.Debug.WriteLine("WindowHelper.ForceWindowToForeground: Advanced activation technique completed");
                    }
                    finally
                    {
                        // Detach input threads if we attached them
                        if (inputAttached && foregroundThreadId != 0 && currentThreadId != 0)
                        {
                            try
                            {
                                AttachThreadInput(currentThreadId, foregroundThreadId, false);
                                System.Diagnostics.Debug.WriteLine("WindowHelper.ForceWindowToForeground: Input threads detached");
                            }
                            catch (Exception detachEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"WindowHelper.ForceWindowToForeground: Error detaching threads: {detachEx.Message}");
                            }
                        }
                    }
                }
                finally
                {
                    // Restore the original foreground lock timeout
                    if (timeoutChanged)
                    {
                        try
                        {
                            SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, ref originalLockTimeout, SPIF_SENDCHANGE);
                            System.Diagnostics.Debug.WriteLine("WindowHelper.ForceWindowToForeground: Foreground lock timeout restored");
                        }
                        catch (Exception restoreEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"WindowHelper.ForceWindowToForeground: Error restoring timeout: {restoreEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WindowHelper.ForceWindowToForeground: Error in advanced activation: {ex.Message}");

                // Final fallback - just try the basic calls
                try
                {
                    System.Diagnostics.Debug.WriteLine("WindowHelper.ForceWindowToForeground: Attempting final fallback");
                    BringWindowToTop(windowHandle);
                    SetForegroundWindow(windowHandle);
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"WindowHelper.ForceWindowToForeground: Fallback also failed: {fallbackEx.Message}");
                }
            }
        }

        /// <summary>
        /// Checks if a window is currently minimized
        /// </summary>
        /// <param name="windowHandle">Handle to the window</param>
        /// <returns>True if the window is minimized, false otherwise</returns>
        public static bool IsWindowMinimized(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return false;

            try
            {
                return IsIconic(windowHandle);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WindowHelper.IsWindowMinimized: Error checking window state: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if a window is currently visible
        /// </summary>
        /// <param name="windowHandle">Handle to the window</param>
        /// <returns>True if the window is visible, false otherwise</returns>
        public static bool IsWindowVisibleHelper(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return false;

            try
            {
                return IsWindowVisible(windowHandle);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WindowHelper.IsWindowVisibleHelper: Error checking window visibility: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the current window placement information
        /// </summary>
        /// <param name="windowHandle">Handle to the window</param>
        /// <returns>Window placement information, or null if failed</returns>
        public static WindowPlacementInfo GetWindowPlacementInfo(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return null;

            try
            {
                var placement = WINDOWPLACEMENT.Default;

                if (GetWindowPlacement(windowHandle, ref placement))
                {
                    return new WindowPlacementInfo
                    {
                        IsMinimized = placement.showCmd == SW_SHOWMINIMIZED,
                        IsMaximized = placement.showCmd == SW_SHOWMAXIMIZED,
                        IsNormal = placement.showCmd == SW_SHOWNORMAL,
                        Left = placement.rcNormalPosition.Left,
                        Top = placement.rcNormalPosition.Top,
                        Right = placement.rcNormalPosition.Right,
                        Bottom = placement.rcNormalPosition.Bottom
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WindowHelper.GetWindowPlacementInfo: Error getting window placement: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Minimizes a window
        /// </summary>
        /// <param name="windowHandle">Handle to the window</param>
        public static void MinimizeWindow(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return;

            try
            {
                System.Diagnostics.Debug.WriteLine($"WindowHelper.MinimizeWindow: Minimizing window 0x{windowHandle.ToInt64():X}");
                ShowWindow(windowHandle, SW_MINIMIZE);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WindowHelper.MinimizeWindow: Error minimizing window: {ex.Message}");
            }
        }

        /// <summary>
        /// Hides a window
        /// </summary>
        /// <param name="windowHandle">Handle to the window</param>
        public static void HideWindow(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return;

            try
            {
                System.Diagnostics.Debug.WriteLine($"WindowHelper.HideWindow: Hiding window 0x{windowHandle.ToInt64():X}");
                ShowWindow(windowHandle, SW_HIDE);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WindowHelper.HideWindow: Error hiding window: {ex.Message}");
            }
        }

        /// <summary>
        /// Flashes a window to draw user attention
        /// </summary>
        /// <param name="windowHandle">Handle to the window</param>
        public static void FlashWindowForAttention(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return;

            try
            {
                // Flash the window a few times
                for (int i = 0; i < 3; i++)
                {
                    FlashWindow(windowHandle, true);
                    System.Threading.Thread.Sleep(100);
                    FlashWindow(windowHandle, false);
                    System.Threading.Thread.Sleep(100);
                }

                System.Diagnostics.Debug.WriteLine($"WindowHelper.FlashWindowForAttention: Window flashed for attention");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WindowHelper.FlashWindowForAttention: Error flashing window: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the handle of the currently active foreground window
        /// </summary>
        /// <returns>Handle to the foreground window, or IntPtr.Zero if none</returns>
        public static IntPtr GetCurrentForegroundWindow()
        {
            try
            {
                return GetForegroundWindow();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WindowHelper.GetCurrentForegroundWindow: Error getting foreground window: {ex.Message}");
                return IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Represents window placement information
    /// </summary>
    public class WindowPlacementInfo
    {
        public bool IsMinimized { get; set; }
        public bool IsMaximized { get; set; }
        public bool IsNormal { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }

        public int Width => Right - Left;
        public int Height => Bottom - Top;

        /// <summary>
        /// Checks if the window position is valid (on screen)
        /// </summary>
        public bool IsValidPosition()
        {
            return Left > -1000 && Top > -1000 &&
                   Width > 100 && Width < 10000 &&
                   Height > 100 && Height < 10000;
        }

        public override string ToString()
        {
            var state = IsMinimized ? "Minimized" : IsMaximized ? "Maximized" : "Normal";
            return $"WindowPlacement: {state}, Bounds=({Left},{Top},{Width}x{Height})";
        }
    }
}