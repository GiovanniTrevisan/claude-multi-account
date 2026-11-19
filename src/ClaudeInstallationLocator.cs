using System;
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
        // The Store package is published as "AnthropicPBC.Claude"; plain "Claude"
        // is kept as a fallback in case a future/older package uses it.
        private static readonly string[] KnownPackageNames = { "AnthropicPBC.Claude", "Claude" };

        private const int PowerShellTimeoutMilliseconds = 15000;

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
            catch (Exception exception)
            {
                DiagnosticLog.Write("Could not read claude.exe path cache: " + exception.Message);
                return null;
            }
        }

        private static void WriteCache(string cacheFilePath, string resolvedPath)
        {
            try
            {
                File.WriteAllText(cacheFilePath, resolvedPath);
            }
            catch (Exception exception)
            {
                // Best-effort cache; a failed write just means we re-resolve next time.
                DiagnosticLog.Write("Could not write claude.exe path cache: " + exception.Message);
            }
        }

        private static string ResolveViaPowerShell()
        {
            var script =
                "foreach ($n in @('" + string.Join("','", KnownPackageNames) + "')) { " +
                "$p = Get-AppxPackage -Name $n | Select-Object -First 1; " +
                "if ($p) { Join-Path $p.InstallLocation 'app\\claude.exe'; break } }";

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
                    // Output is a single short line, so waiting before reading
                    // cannot deadlock on a full pipe buffer.
                    if (!process.WaitForExit(PowerShellTimeoutMilliseconds))
                    {
                        process.Kill();
                        DiagnosticLog.Write("Get-AppxPackage timed out.");
                        return null;
                    }

                    var output = process.StandardOutput.ReadToEnd().Trim();
                    return output.Length > 0 && File.Exists(output) ? output : null;
                }
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write("Get-AppxPackage lookup failed: " + exception.Message);
                return null;
            }
        }
    }
}
