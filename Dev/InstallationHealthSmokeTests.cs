using System;
using System.IO;

namespace PPGTogether.BepInEx
{
    internal static class InstallationHealthSmokeTests
    {
        private static int Main()
        {
            string root = Path.Combine(Path.GetTempPath(), "ConnectInstallationHealth-" + Guid.NewGuid().ToString("N"));
            try
            {
                string core = Path.Combine(root, "BepInEx", "core");
                string pluginDirectory = Path.Combine(root, "BepInEx", "plugins", "Connect");
                string pluginAssembly = Path.Combine(pluginDirectory, "Connect.BepInEx.dll");
                Directory.CreateDirectory(core);
                Directory.CreateDirectory(pluginDirectory);
                File.WriteAllBytes(Path.Combine(core, "BepInEx.dll"), new byte[0]);
                File.WriteAllBytes(pluginAssembly, new byte[0]);
                File.WriteAllBytes(Path.Combine(pluginDirectory, "connect-icon.png"), new byte[0]);

                InstallationHealth complete = InstallationHealth.Check(root, pluginDirectory, pluginAssembly);
                if (complete.RequiresRecovery || complete.MissingParts().Count != 0) return 1;
                if (!ConnectSupportLinks.IsSafeGitHubUrl(ConnectSupportLinks.PublishedReleaseUrl)) return 2;
                if (ConnectSupportLinks.PublishedReleaseUrl.IndexOf("Connect-v0.1.47.zip", StringComparison.Ordinal) < 0) return 3;
                if (ConnectSupportLinks.IsSafeGitHubUrl("http://github.com/ForCesCustom/PPG-Connect")) return 4;
                if (ConnectSupportLinks.IsSafeGitHubUrl("https://github.com.evil.example/ForCesCustom/PPG-Connect")) return 5;

                File.Delete(Path.Combine(pluginDirectory, "connect-icon.png"));
                InstallationHealth missingIcon = InstallationHealth.Check(root, pluginDirectory, pluginAssembly);
                if (!missingIcon.RequiresRecovery || missingIcon.MissingParts().Count != 1 || missingIcon.MissingParts()[0] != "connect-icon.png") return 6;
                return 0;
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
