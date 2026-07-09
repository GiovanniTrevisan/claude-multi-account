using System.Diagnostics;
using System.IO;

namespace ClaudeMultiAccount
{
    /// <summary>
    /// Resolves the path to the officially installed claude.exe (Microsoft Store
    /// MSIX package). The result is cached inside the isolated profile directory
    /// and re-validated on every run, since a Claude update changes the versioned
    /// install path.
    /// </summary>
    internal static class ClaudeInstallationLocator
    {
        public static string Resolve(string cacheFilePath)
        {
            var cachedPath = ReadCache(cacheFilePath);
            if (cachedPath != null)
                return cachedPath;

            var resolvedPath = ResolveViaPowerShell();
            if (resolvedPath != null)
                WriteCache(cacheFilePath, resolvedPath);
            return resolvedPath;
        }

        private static string ReadCache(string cacheFilePath)
        {
            try
            {
                if (!File.Exists(cacheFilePath))
                    return null;
                var cachedPath = File.ReadAllText(cacheFilePath).Trim();
                return cachedPath.Length > 0 && File.Exists(cachedPath) ? cachedPath : null;
            }
            catch
            {
                return null;
            }
        }

        private static void WriteCache(string cacheFilePath, string resolvedPath)
        {
            try
            {
                File.WriteAllText(cacheFilePath, resolvedPath);
            }
            catch
            {
                // Best-effort cache; a failed write just means we re-resolve next time.
            }
        }

        private static string ResolveViaPowerShell()
        {
            const string script =
                "$p = Get-AppxPackage -Name Claude | Select-Object -First 1; " +
                "if ($p) { Join-Path $p.InstallLocation 'app\\claude.exe' }";

            try
            {
                var startInfo = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -Command \"" + script + "\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(15000);
                    return output.Length > 0 && File.Exists(output) ? output : null;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
