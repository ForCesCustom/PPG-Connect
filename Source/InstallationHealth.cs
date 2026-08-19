using System;
using System.Collections.Generic;
using System.IO;

namespace PPGTogether.BepInEx
{
    internal sealed class InstallationHealth
    {
        internal bool BepInExCoreFound;
        internal bool PluginDirectoryFound;
        internal bool PluginAssemblyFound;
        internal bool IconFound;
        internal string GameRoot;
        internal string PluginDirectory;

        internal bool RequiresRecovery
        {
            get { return !BepInExCoreFound || !PluginDirectoryFound || !PluginAssemblyFound || !IconFound; }
        }

        internal static InstallationHealth Check(string gameRoot, string pluginDirectory, string pluginAssemblyPath)
        {
            InstallationHealth health = new InstallationHealth();
            health.GameRoot = gameRoot ?? string.Empty;
            health.PluginDirectory = pluginDirectory ?? string.Empty;
            health.BepInExCoreFound = File.Exists(Path.Combine(health.GameRoot, "BepInEx", "core", "BepInEx.dll"));
            health.PluginDirectoryFound = Directory.Exists(health.PluginDirectory);
            health.PluginAssemblyFound = !string.IsNullOrEmpty(pluginAssemblyPath) && File.Exists(pluginAssemblyPath);
            health.IconFound = health.PluginDirectoryFound && File.Exists(Path.Combine(health.PluginDirectory, "connect-icon.png"));
            return health;
        }

        internal List<string> MissingParts()
        {
            List<string> missing = new List<string>();
            if (!BepInExCoreFound) missing.Add("BepInEx 5 loader (BepInEx\\core\\BepInEx.dll)");
            if (!PluginDirectoryFound) missing.Add("Connect plugin folder (BepInEx\\plugins\\Connect)");
            if (!PluginAssemblyFound) missing.Add("Connect.BepInEx.dll");
            if (!IconFound) missing.Add("connect-icon.png");
            return missing;
        }
    }

    internal static class ConnectSupportLinks
    {
        // Canonical direct download for the complete plug-and-play package.
        // It is maintained with every public Connect update.
        internal const string PublishedReleaseUrl = "https://github.com/ForCesCustom/PPG-Connect/raw/main/Releases/Connect-v0.1.37.zip";
        internal const string BepInExInstallGuideUrl = "https://docs.bepinex.dev/master/articles/user_guide/installation/unity_mono.html?tabs=tabid-win";

        internal static bool IsSafeGitHubUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
            return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) || string.Equals(uri.Host, "www.github.com", StringComparison.OrdinalIgnoreCase);
        }
    }
}
