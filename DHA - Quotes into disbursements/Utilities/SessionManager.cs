using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DHA.DSTC.WPF.Utilities
{
    /// <summary>
    /// Manages application session state persistence across application restarts
    /// </summary>
    public static class SessionManager
    {
        private static readonly string SessionFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DHA", "TimeManagement", "session.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
            // Allow reading/writing of null values
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Represents the application's session data
        /// </summary>
        public class SessionData
        {
            /// <summary>
            /// ID of the selected team member
            /// </summary>
            public Guid? SelectedTeamMemberId { get; set; }

            /// <summary>
            /// Last selected date for time entry
            /// </summary>
            public DateTime? LastTimeEntryDate { get; set; }

            /// <summary>
            /// From date filter for time entries
            /// </summary>
            public DateTime? FromDateFilter { get; set; }

            /// <summary>
            /// To date filter for time entries
            /// </summary>
            public DateTime? ToDateFilter { get; set; }

            /// <summary>
            /// Index of the selected tab in the main tab control
            /// </summary>
            public int SelectedTabIndex { get; set; }

            /// <summary>
            /// Last selected project ID for disbursements
            /// </summary>
            public Guid? LastSelectedProjectId { get; set; }

            /// <summary>
            /// Last selected disbursement type ID
            /// </summary>
            public int? LastSelectedDisbursementTypeId { get; set; }

            /// <summary>
            /// Window state information
            /// </summary>
            public WindowState WindowState { get; set; } = new WindowState();

            /// <summary>
            /// Application preferences
            /// </summary>
            public UserPreferences Preferences { get; set; } = new UserPreferences();

            /// <summary>
            /// When this session was last saved
            /// </summary>
            public DateTime LastSaved { get; set; } = DateTime.Now;

            /// <summary>
            /// Version of the application that saved this session
            /// </summary>
            public string ApplicationVersion { get; set; }

            /// <summary>
            /// Creates a new session data instance with default values
            /// </summary>
            public SessionData()
            {
                ApplicationVersion = GetApplicationVersion();
            }

            /// <summary>
            /// Validates that the session data is not too old
            /// </summary>
            /// <param name="maxAge">Maximum age of session data to accept</param>
            /// <returns>True if session is valid, false if too old</returns>
            public bool IsValid(TimeSpan? maxAge = null)
            {
                var maximumAge = maxAge ?? TimeSpan.FromDays(30);
                return DateTime.Now - LastSaved <= maximumAge;
            }

            /// <summary>
            /// Updates the LastSaved timestamp to now
            /// </summary>
            public void UpdateSaveTime()
            {
                LastSaved = DateTime.Now;
                ApplicationVersion = GetApplicationVersion();
            }
        }

        /// <summary>
        /// Represents window state information
        /// </summary>
        public class WindowState
        {
            public double? Left { get; set; }
            public double? Top { get; set; }
            public double? Width { get; set; }
            public double? Height { get; set; }
            public string WindowStateString { get; set; } = "Normal";
            public bool? IsMaximized { get; set; }
            public bool? IsMinimized { get; set; }

            /// <summary>
            /// Validates that the window position is reasonable (on screen)
            /// </summary>
            public bool IsValidPosition()
            {
                // Basic validation - ensure values are positive and reasonable
                return Left >= -100 && Top >= -100 &&
                       Width > 200 && Width < 5000 &&
                       Height > 150 && Height < 5000;
            }
        }

        /// <summary>
        /// Represents user preferences
        /// </summary>
        public class UserPreferences
        {
            /// <summary>
            /// Whether to start minimized to system tray
            /// </summary>
            public bool StartMinimized { get; set; } = false;

            /// <summary>
            /// Whether to remember window position
            /// </summary>
            public bool RememberWindowPosition { get; set; } = true;

            /// <summary>
            /// Default expected hours per day
            /// </summary>
            public decimal DefaultExpectedHours { get; set; } = 8.0m;

            /// <summary>
            /// Whether to show detailed logging
            /// </summary>
            public bool EnableDetailedLogging { get; set; } = false;

            /// <summary>
            /// Auto-refresh interval in minutes (0 = disabled)
            /// </summary>
            public int AutoRefreshIntervalMinutes { get; set; } = 15;
        }

        /// <summary>
        /// Saves the current session data to disk
        /// </summary>
        /// <param name="sessionData">Session data to save</param>
        /// <returns>True if saved successfully, false otherwise</returns>
        public static bool SaveSession(SessionData sessionData)
        {
            if (sessionData == null)
            {
                System.Diagnostics.Debug.WriteLine("SessionManager.SaveSession: Cannot save null session data");
                return false;
            }

            try
            {
                // Update save timestamp
                sessionData.UpdateSaveTime();

                // Ensure directory exists
                var directory = Path.GetDirectoryName(SessionFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    System.Diagnostics.Debug.WriteLine($"SessionManager.SaveSession: Created directory: {directory}");
                }

                // Serialize to JSON
                var json = JsonSerializer.Serialize(sessionData, JsonOptions);

                // Write to temporary file first, then move (atomic operation)
                var tempFile = SessionFilePath + ".tmp";
                File.WriteAllText(tempFile, json);

                // Replace existing file
                if (File.Exists(SessionFilePath))
                {
                    File.Delete(SessionFilePath);
                }
                File.Move(tempFile, SessionFilePath);

                System.Diagnostics.Debug.WriteLine($"SessionManager.SaveSession: Session saved successfully to: {SessionFilePath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SessionManager.SaveSession: Failed to save session: {ex.Message}");

                // Clean up temporary file if it exists
                try
                {
                    var tempFile = SessionFilePath + ".tmp";
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }

                return false;
            }
        }

        /// <summary>
        /// Loads session data from disk
        /// </summary>
        /// <param name="maxAge">Maximum age of session data to accept (default: 30 days)</param>
        /// <returns>Loaded session data, or new instance if loading failed</returns>
        public static SessionData LoadSession(TimeSpan? maxAge = null)
        {
            try
            {
                if (!File.Exists(SessionFilePath))
                {
                    System.Diagnostics.Debug.WriteLine("SessionManager.LoadSession: No session file found, creating new session");
                    return new SessionData();
                }

                System.Diagnostics.Debug.WriteLine($"SessionManager.LoadSession: Loading session from: {SessionFilePath}");

                // Read and deserialize
                var json = File.ReadAllText(SessionFilePath);
                var sessionData = JsonSerializer.Deserialize<SessionData>(json, JsonOptions);

                if (sessionData == null)
                {
                    System.Diagnostics.Debug.WriteLine("SessionManager.LoadSession: Deserialized session data is null, creating new session");
                    return new SessionData();
                }

                // Validate session age
                if (!sessionData.IsValid(maxAge))
                {
                    var age = DateTime.Now - sessionData.LastSaved;
                    System.Diagnostics.Debug.WriteLine($"SessionManager.LoadSession: Session data too old ({age.TotalDays:F1} days), creating new session");
                    return new SessionData();
                }

                // Validate window state if present
                if (sessionData.WindowState != null && !sessionData.WindowState.IsValidPosition())
                {
                    System.Diagnostics.Debug.WriteLine("SessionManager.LoadSession: Invalid window state detected, resetting to defaults");
                    sessionData.WindowState = new WindowState();
                }

                System.Diagnostics.Debug.WriteLine($"SessionManager.LoadSession: Session restored successfully (saved: {sessionData.LastSaved})");
                return sessionData;
            }
            catch (JsonException jsonEx)
            {
                System.Diagnostics.Debug.WriteLine($"SessionManager.LoadSession: JSON parsing error: {jsonEx.Message}");

                // Try to backup the corrupted file
                BackupCorruptedSession();

                return new SessionData();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SessionManager.LoadSession: Failed to load session: {ex.Message}");
                return new SessionData();
            }
        }

        /// <summary>
        /// Clears the saved session data
        /// </summary>
        /// <returns>True if cleared successfully, false otherwise</returns>
        public static bool ClearSession()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                {
                    File.Delete(SessionFilePath);
                    System.Diagnostics.Debug.WriteLine($"SessionManager.ClearSession: Session file deleted: {SessionFilePath}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("SessionManager.ClearSession: No session file to delete");
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SessionManager.ClearSession: Failed to clear session: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Backs up a corrupted session file for debugging
        /// </summary>
        private static void BackupCorruptedSession()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                {
                    var backupPath = SessionFilePath + $".corrupted.{DateTime.Now:yyyyMMdd_HHmmss}";
                    File.Copy(SessionFilePath, backupPath);
                    System.Diagnostics.Debug.WriteLine($"SessionManager.BackupCorruptedSession: Corrupted session backed up to: {backupPath}");

                    // Delete the original corrupted file
                    File.Delete(SessionFilePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SessionManager.BackupCorruptedSession: Failed to backup corrupted session: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current application version
        /// </summary>
        /// <returns>Application version string</returns>
        private static string GetApplicationVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Gets information about the session file
        /// </summary>
        /// <returns>Session file information, or null if file doesn't exist</returns>
        public static SessionFileInfo GetSessionFileInfo()
        {
            try
            {
                if (!File.Exists(SessionFilePath))
                    return null;

                var fileInfo = new FileInfo(SessionFilePath);
                return new SessionFileInfo
                {
                    FilePath = SessionFilePath,
                    FileSize = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTime,
                    Exists = true
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SessionManager.GetSessionFileInfo: Error getting file info: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Information about the session file
        /// </summary>
        public class SessionFileInfo
        {
            public string FilePath { get; set; }
            public long FileSize { get; set; }
            public DateTime LastModified { get; set; }
            public bool Exists { get; set; }

            public string FileSizeFormatted => FileSize < 1024 ? $"{FileSize} bytes" : $"{FileSize / 1024:F1} KB";
        }
    }
}