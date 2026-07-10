using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace Winshots.Installer;

internal static class Program
{
    private const string AppName = "Winshots";
    private const string PayloadResourceName = "WinshotsPayload.zip";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Winshots";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try
        {
            InstallerOptions options = InstallerOptions.Parse(args);
            if (options.VerifyPayload)
            {
                return VerifyPayload();
            }

            return options.Uninstall ? Uninstall(options) : Install(options);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Winshots Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int Install(InstallerOptions options)
    {
        string installRoot = ResolveFullPath(options.InstallRoot ?? DefaultInstallRoot());
        EnsureSafeInstallRoot(installRoot);

        if (!options.Silent)
        {
            DialogResult result = MessageBox.Show(
                $"Install Winshots for this Windows user?\n\nLocation:\n{installRoot}",
                "Winshots Setup",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);

            if (result != DialogResult.OK)
            {
                return 0;
            }
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), $"Winshots.Setup.{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            using Stream payload = OpenPayload();
            ZipFile.ExtractToDirectory(payload, tempRoot, overwriteFiles: true);
            ValidatePayload(tempRoot);

            if (Directory.Exists(installRoot))
            {
                StopInstalledProcesses(installRoot);
                Directory.Delete(installRoot, recursive: true);
            }

            Directory.CreateDirectory(installRoot);
            CopyDirectory(tempRoot, installRoot);

            RewriteMcpConfig(installRoot);
            CopySetupForUninstall(installRoot);
            CreateStartMenuShortcuts(installRoot);
            RegisterUninstaller(installRoot);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        if (!options.Silent)
        {
            MessageBox.Show(
                "Winshots was installed. Codex plugin registration is intentionally separate; run install.ps1 -InstallCodexPlugin after closing Codex if you want to refresh the local plugin cache.",
                "Winshots Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        return 0;
    }

    private static void StopInstalledProcesses(string installRoot)
    {
        using Process currentProcess = Process.GetCurrentProcess();
        var installedProcesses = new List<Process>();

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                string? executablePath = process.MainModule?.FileName;
                if (process.Id != currentProcess.Id &&
                    !string.IsNullOrWhiteSpace(executablePath) &&
                    IsUnderRoot(installRoot, executablePath))
                {
                    installedProcesses.Add(process);
                    process.CloseMainWindow();
                    continue;
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited while it was being inspected.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Processes outside this user's session may not expose their executable path.
            }

            process.Dispose();
        }

        foreach (Process process in installedProcesses)
        {
            using (process)
            {
                if (!process.WaitForExit(3000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
        }
    }

    private static int Uninstall(InstallerOptions options)
    {
        string installRoot = ResolveFullPath(options.InstallRoot ?? ReadInstallRootFromRegistry() ?? DefaultInstallRoot());
        EnsureSafeInstallRoot(installRoot);

        if (!options.Silent)
        {
            DialogResult result = MessageBox.Show(
                $"Uninstall Winshots from this Windows user?\n\nLocation:\n{installRoot}",
                "Winshots Setup",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);

            if (result != DialogResult.OK)
            {
                return 0;
            }
        }

        RemoveStartMenuShortcuts();
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);

        if (Directory.Exists(installRoot))
        {
            string currentExe = Environment.ProcessPath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(currentExe) && IsUnderRoot(installRoot, currentExe))
            {
                ScheduleInstallRootRemoval(installRoot);
            }
            else
            {
                Directory.Delete(installRoot, recursive: true);
            }
        }

        if (!options.Silent)
        {
            MessageBox.Show("Winshots was uninstalled.", "Winshots Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        return 0;
    }

    private static int VerifyPayload()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"Winshots.Setup.Verify.{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            using Stream payload = OpenPayload();
            ZipFile.ExtractToDirectory(payload, tempRoot, overwriteFiles: true);
            ValidatePayload(tempRoot);
            return 0;
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static Stream OpenPayload()
    {
        Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName);
        return stream ?? throw new InvalidOperationException("The Winshots setup payload is missing. Rebuild the setup executable with scripts\\build-release.ps1.");
    }

    private static void ValidatePayload(string root)
    {
        string appExe = Path.Combine(root, "app", "Winshots.App.exe");
        string mcpExe = Path.Combine(root, "mcp", "Winshots.Mcp.exe");
        if (!File.Exists(appExe) || !File.Exists(mcpExe))
        {
            throw new InvalidOperationException("The Winshots setup payload is incomplete.");
        }
    }

    private static void RewriteMcpConfig(string installRoot)
    {
        string mcpExe = Path.Combine(installRoot, "mcp", "Winshots.Mcp.exe");
        var mcpJson = new
        {
            mcpServers = new
            {
                winshots = new
                {
                    command = mcpExe,
                    args = Array.Empty<string>(),
                    cwd = installRoot,
                    startup_timeout_sec = 60.0,
                    tool_timeout_sec = 120
                }
            }
        };

        File.WriteAllText(
            Path.Combine(installRoot, ".mcp.json"),
            JsonSerializer.Serialize(mcpJson, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CopySetupForUninstall(string installRoot)
    {
        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
        {
            return;
        }

        string target = Path.Combine(installRoot, "Winshots.Setup.exe");
        if (!Path.GetFullPath(currentExe).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(currentExe, target, overwrite: true);
        }
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        foreach (string directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void CreateStartMenuShortcuts(string installRoot)
    {
        string shortcutFolder = StartMenuShortcutFolder();
        Directory.CreateDirectory(shortcutFolder);

        CreateShortcut(
            Path.Combine(shortcutFolder, "Winshots.lnk"),
            Path.Combine(installRoot, "app", "Winshots.App.exe"),
            string.Empty,
            Path.Combine(installRoot, "app"));

        string reviewLauncher = Path.Combine(installRoot, "Winshots Review UI.cmd");
        if (File.Exists(reviewLauncher))
        {
            CreateShortcut(
                Path.Combine(shortcutFolder, "Winshots Review UI.lnk"),
                reviewLauncher,
                string.Empty,
                installRoot);
        }

        CreateShortcut(
            Path.Combine(shortcutFolder, "Uninstall Winshots.lnk"),
            Path.Combine(installRoot, "Winshots.Setup.exe"),
            "--uninstall",
            installRoot);
    }

    private static void RemoveStartMenuShortcuts()
    {
        string shortcutFolder = StartMenuShortcutFolder();
        if (Directory.Exists(shortcutFolder))
        {
            Directory.Delete(shortcutFolder, recursive: true);
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string arguments, string workingDirectory)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            throw new InvalidOperationException("Windows Script Host is not available to create Start Menu shortcuts.");
        }

        object shell = Activator.CreateInstance(shellType)!;
        object? shortcut = null;
        try
        {
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            Type shortcutType = shortcut!.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, Array.Empty<object>());
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static void RegisterUninstaller(string installRoot)
    {
        string setupPath = Path.Combine(installRoot, "Winshots.Setup.exe");
        string appPath = Path.Combine(installRoot, "app", "Winshots.App.exe");
        Version version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not create the Winshots uninstall registry key.");

        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", $"{version.Major}.{version.Minor}.{version.Build}");
        key.SetValue("Publisher", "Winshots");
        key.SetValue("InstallLocation", installRoot);
        key.SetValue("DisplayIcon", appPath);
        key.SetValue("UninstallString", $"\"{setupPath}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{setupPath}\" --uninstall --silent");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static string? ReadInstallRootFromRegistry()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
        return key?.GetValue("InstallLocation") as string;
    }

    private static void ScheduleInstallRootRemoval(string installRoot)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                CreateNoWindow = true,
                UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add("Start-Sleep -Seconds 2; Remove-Item -LiteralPath $args[0] -Recurse -Force");
        process.StartInfo.ArgumentList.Add(installRoot);

        process.Start();
    }

    private static string DefaultInstallRoot()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Programs", "Winshots");
    }

    private static string StartMenuShortcutFolder()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Winshots");
    }

    private static string ResolveFullPath(string path)
    {
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    private static void EnsureSafeInstallRoot(string installRoot)
    {
        string rootPath = Path.GetPathRoot(installRoot) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(installRoot) ||
            installRoot.Equals(rootPath, StringComparison.OrdinalIgnoreCase) ||
            installRoot.Length < rootPath.Length + AppName.Length)
        {
            throw new InvalidOperationException($"Install location is not safe: {installRoot}");
        }
    }

    private static bool IsUnderRoot(string root, string target)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedTarget = Path.GetFullPath(target);
        return normalizedTarget.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedTarget.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record InstallerOptions(bool Silent, bool Uninstall, bool VerifyPayload, string? InstallRoot)
    {
        public static InstallerOptions Parse(string[] args)
        {
            bool silent = false;
            bool uninstall = false;
            bool verifyPayload = false;
            string? installRoot = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "/S", StringComparison.OrdinalIgnoreCase))
                {
                    silent = true;
                }
                else if (string.Equals(arg, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "/uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    uninstall = true;
                }
                else if (string.Equals(arg, "--verify-payload", StringComparison.OrdinalIgnoreCase))
                {
                    verifyPayload = true;
                    silent = true;
                }
                else if (string.Equals(arg, "--install-root", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    installRoot = args[++i];
                }
                else if (arg.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase))
                {
                    installRoot = arg["/DIR=".Length..].Trim('"');
                }
            }

            return new InstallerOptions(silent, uninstall, verifyPayload, installRoot);
        }
    }
}
