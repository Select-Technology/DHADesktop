using System;

namespace DHA.DSTC.WPF.Updates
{
    public class VersionManifest
    {
        public string LatestVersion { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseNotes { get; set; }
        public string MinimumVersion { get; set; }
        public bool ForceUpdate { get; set; }
    }

    public class UpdateInfo
    {
        public bool UpdateAvailable { get; set; }
        public string LatestVersion { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseNotes { get; set; }
        public bool IsForced { get; set; }
    }
}