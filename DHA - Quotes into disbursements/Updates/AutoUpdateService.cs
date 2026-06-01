using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using DHA.DSTC.WPF.ProjectProperties;
using Newtonsoft.Json;

namespace DHA.DSTC.WPF.Updates
{
    public class AutoUpdateService
    {
        private readonly string _versionManifestUrl;
        private readonly string _currentVersion;

        public AutoUpdateService()
        {
            _versionManifestUrl = Settings.Default.UpdateManifestUrl;
            _currentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }

        public async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Checking for updates. Current version: {_currentVersion}");
                System.Diagnostics.Debug.WriteLine($"Manifest URL: {_versionManifestUrl}");

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(30);

                    var json = await client.GetStringAsync(_versionManifestUrl);
                    var manifest = JsonConvert.DeserializeObject<VersionManifest>(json);

                    var current = new Version(_currentVersion);
                    var latest = new Version(manifest.LatestVersion);

                    System.Diagnostics.Debug.WriteLine($"Latest version available: {manifest.LatestVersion}");
                    System.Diagnostics.Debug.WriteLine($"Update available: {latest > current}");

                    return new UpdateInfo
                    {
                        UpdateAvailable = latest > current,
                        LatestVersion = manifest.LatestVersion,
                        DownloadUrl = manifest.DownloadUrl,
                        ReleaseNotes = manifest.ReleaseNotes,
                        IsForced = manifest.ForceUpdate
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
                return new UpdateInfo { UpdateAvailable = false };
            }
        }

        public async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl)
        {
            try
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"DHA.DSTC.Update.{Guid.NewGuid()}.msi");

                System.Diagnostics.Debug.WriteLine($"Downloading update to: {tempPath}");

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(10);

                    var fileBytes = await client.GetByteArrayAsync(downloadUrl);
                    File.WriteAllBytes(tempPath, fileBytes);
                }

                System.Diagnostics.Debug.WriteLine($"Download complete. File size: {new FileInfo(tempPath).Length} bytes");

                return InstallUpdate(tempPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Update failed: {ex.Message}", "Update Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool InstallUpdate(string msiPath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Installing per-user update: {msiPath}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = $"/i \"{msiPath}\" /quiet /norestart REBOOT=ReallySuppress",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = Process.Start(startInfo);
                process.WaitForExit();

                System.Diagnostics.Debug.WriteLine($"MSI exit code: {process.ExitCode}");

                // Clean up temp file
                try { File.Delete(msiPath); } catch { }

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Install failed: {ex.Message}");
                return false;
            }
        }
    }
}