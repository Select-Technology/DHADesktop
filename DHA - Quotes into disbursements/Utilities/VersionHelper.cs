using System;
using System.Reflection;

namespace DHA.DSTC.WPF.Utilities
{
    /// <summary>
    /// Utility class for retrieving application version information
    /// </summary>
    public static class VersionHelper
    {
        private static string _cachedVersion;
        private static string _cachedFullVersion;

        /// <summary>
        /// Gets the application version in short format (e.g., "1.2.3")
        /// </summary>
        public static string GetVersion()
        {
            if (string.IsNullOrEmpty(_cachedVersion))
            {
                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var version = assembly.GetName().Version;

                    if (version != null)
                    {
                        // Return Major.Minor.Build format (skip revision if it's 0)
                        if (version.Revision == 0 && version.Build == 0)
                        {
                            _cachedVersion = $"{version.Major}.{version.Minor}";
                        }
                        else if (version.Revision == 0)
                        {
                            _cachedVersion = $"{version.Major}.{version.Minor}.{version.Build}";
                        }
                        else
                        {
                            _cachedVersion = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
                        }
                    }
                    else
                    {
                        _cachedVersion = "1.0.0";
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"VersionHelper.GetVersion: Error getting version: {ex.Message}");
                    _cachedVersion = "1.0.0";
                }
            }

            return _cachedVersion;
        }

        /// <summary>
        /// Gets the full application version including file version (e.g., "1.2.3.4567")
        /// </summary>
        public static string GetFullVersion()
        {
            if (string.IsNullOrEmpty(_cachedFullVersion))
            {
                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var assemblyVersion = assembly.GetName().Version;
                    var fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);

                    if (fileVersionInfo != null && !string.IsNullOrEmpty(fileVersionInfo.FileVersion))
                    {
                        _cachedFullVersion = fileVersionInfo.FileVersion;
                    }
                    else if (assemblyVersion != null)
                    {
                        _cachedFullVersion = assemblyVersion.ToString();
                    }
                    else
                    {
                        _cachedFullVersion = "1.0.0.0";
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"VersionHelper.GetFullVersion: Error getting full version: {ex.Message}");
                    _cachedFullVersion = "1.0.0.0";
                }
            }

            return _cachedFullVersion;
        }

        /// <summary>
        /// Gets the version formatted for display in the status bar (e.g., "v1.2.3")
        /// </summary>
        public static string GetDisplayVersion()
        {
            return $"v{GetVersion()}";
        }

        /// <summary>
        /// Gets the full application title with version (e.g., "DHA Time Management v1.2.3")
        /// </summary>
        public static string GetApplicationTitle()
        {
            return $"DHA Time Management {GetDisplayVersion()}";
        }

        /// <summary>
        /// Clears the cached version information (useful for testing or if version changes at runtime)
        /// </summary>
        public static void ClearCache()
        {
            _cachedVersion = null;
            _cachedFullVersion = null;
        }

        /// <summary>
        /// Gets build date information if available
        /// </summary>
        public static DateTime? GetBuildDate()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var fileInfo = new System.IO.FileInfo(assembly.Location);
                return fileInfo.LastWriteTime;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VersionHelper.GetBuildDate: Error getting build date: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets a detailed version string with build information
        /// </summary>
        public static string GetDetailedVersionInfo()
        {
            var version = GetDisplayVersion();
            var buildDate = GetBuildDate();

            if (buildDate.HasValue)
            {
                return $"{version} (Built: {buildDate.Value:yyyy-MM-dd HH:mm})";
            }

            return version;
        }
    }
}