using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DevLauncher.Services;

/// <summary>Path settings that can be checked and detected automatically.</summary>
public enum DetectableSetting
{
    XamppDirectory,
    ApacheExecutable,
    MySqlExecutable,
    MySqlConfiguration,
    FileZillaExecutable,
    XamppPanel,
    VSCodeExecutable,
    VisualStudioExecutable,
    ChromeExecutable,
    FirefoxExecutable,
}

/// <summary>What a path setting must point to.</summary>
public enum SettingPathKind
{
    File,
    Folder,
    /// <summary>An existing file, or a command name found in the PATH.</summary>
    Executable,
}

/// <summary>
/// Checks the path settings and finds the installed tools : XAMPP in its usual folders or from its installer key,
/// the editors and browsers from the PATH, the registered application paths and their usual install folders.
/// </summary>
public static class SettingsAutoDetector
{
    private const string AppPathsKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    /// <summary>XAMPP executables and files, relative to the XAMPP folder.</summary>
    private static readonly (DetectableSetting Setting, string RelativePath)[] _xamppComponents =
    {
        (DetectableSetting.ApacheExecutable, @"apache\bin\httpd.exe"),
        (DetectableSetting.MySqlExecutable, @"mysql\bin\mysqld.exe"),
        (DetectableSetting.MySqlConfiguration, @"mysql\bin\my.ini"),
        (DetectableSetting.FileZillaExecutable, @"FileZillaFTP\FileZillaServer.exe"),
        (DetectableSetting.XamppPanel, "xampp-control.exe"),
    };

    public static SettingPathKind GetPathKind(DetectableSetting setting) => setting switch
    {
        DetectableSetting.XamppDirectory => SettingPathKind.Folder,
        DetectableSetting.VSCodeExecutable => SettingPathKind.Executable,
        _ => SettingPathKind.File,
    };

    /// <summary>Returns true when the value points to what the setting expects.</summary>
    public static bool IsValid(SettingPathKind pathKind, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmedValue = value.Trim().Trim('"');
        try
        {
            return pathKind switch
            {
                SettingPathKind.Folder => Directory.Exists(trimmedValue),
                SettingPathKind.File => File.Exists(trimmedValue),
                _ => ExecutableLocator.IsExecutableAvailable(trimmedValue),
            };
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Detects every setting it can. Settings that are not found are absent from the result.</summary>
    public static Dictionary<DetectableSetting, string> DetectAll()
    {
        var detectedPaths = new Dictionary<DetectableSetting, string>();
        var xamppDirectory = FindXamppDirectory(GetXamppDirectoryCandidates());
        if (xamppDirectory is not null)
        {
            detectedPaths[DetectableSetting.XamppDirectory] = xamppDirectory;
            foreach (var (setting, componentPath) in GetXamppComponentPaths(xamppDirectory)) detectedPaths[setting] = componentPath;
        }
        void AddWhenFound(DetectableSetting setting, string? detectedPath)
        {
            if (detectedPath is not null) detectedPaths[setting] = detectedPath;
        }
        AddWhenFound(DetectableSetting.VSCodeExecutable, DetectVSCode());
        AddWhenFound(DetectableSetting.VisualStudioExecutable, DetectVisualStudio());
        AddWhenFound(DetectableSetting.ChromeExecutable, FindRegisteredApplication("chrome.exe")
            ?? FirstExistingFile(ProgramFilesPaths(@"Google\Chrome\Application\chrome.exe").Append(LocalApplicationDataPath(@"Google\Chrome\Application\chrome.exe"))));
        AddWhenFound(DetectableSetting.FirefoxExecutable, FindRegisteredApplication("firefox.exe")
            ?? FirstExistingFile(ProgramFilesPaths(@"Mozilla Firefox\firefox.exe")));
        return detectedPaths;
    }

    /// <summary>Returns the first folder holding a XAMPP installation (its control panel or its Apache server).</summary>
    public static string? FindXamppDirectory(IEnumerable<string> candidateDirectories)
        => candidateDirectories
            .Where(candidateDirectory => !string.IsNullOrWhiteSpace(candidateDirectory))
            .FirstOrDefault(candidateDirectory => File.Exists(Path.Combine(candidateDirectory, "xampp-control.exe"))
                || File.Exists(Path.Combine(candidateDirectory, "apache", "bin", "httpd.exe")));

    /// <summary>Returns the XAMPP components present in a XAMPP folder.</summary>
    public static Dictionary<DetectableSetting, string> GetXamppComponentPaths(string xamppDirectory)
        => _xamppComponents
            .Select(component => (component.Setting, ComponentPath: Path.Combine(new[] { xamppDirectory }.Concat(component.RelativePath.Split('\\')).ToArray())))
            .Where(component => File.Exists(component.ComponentPath))
            .ToDictionary(component => component.Setting, component => component.ComponentPath);

    private static IEnumerable<string> GetXamppDirectoryCandidates()
    {
        var installerDirectory = ReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\xampp", "Install_Dir")
            ?? ReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\xampp", "Install_Dir");
        if (installerDirectory is not null) yield return installerDirectory;
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed))
            yield return Path.Combine(drive.RootDirectory.FullName, "xampp");
    }

    /// <summary>The code command is kept when it is in the PATH : it follows the updates of VSCode.</summary>
    private static string? DetectVSCode()
        => ExecutableLocator.FindInPath("code") is not null
            ? "code"
            : FirstExistingFile(new[] { LocalApplicationDataPath(@"Programs\Microsoft VS Code\Code.exe") }.Concat(ProgramFilesPaths(@"Microsoft VS Code\Code.exe")));

    /// <summary>Asks vswhere (installed with every Visual Studio since 2017) for the most recent installation.</summary>
    private static string? DetectVisualStudio()
    {
        var vswherePath = FirstExistingFile(ProgramFilesPaths(@"Microsoft Visual Studio\Installer\vswhere.exe"));
        if (vswherePath is null) return null;
        try
        {
            var processStartInfo = new ProcessStartInfo(vswherePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            foreach (var argument in new[] { "-latest", "-prerelease", "-property", "productPath" }) processStartInfo.ArgumentList.Add(argument);
            using var vswhereProcess = Process.Start(processStartInfo);
            if (vswhereProcess is null) return null;
            var productPath = vswhereProcess.StandardOutput.ReadToEnd().Trim();
            vswhereProcess.WaitForExit();
            return File.Exists(productPath) ? productPath : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>Reads the path registered by an installer under App Paths (current user, then machine).</summary>
    private static string? FindRegisteredApplication(string executableName)
    {
        foreach (var registryRoot in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            var registeredPath = ReadRegistryValue(registryRoot, $@"{AppPathsKeyPath}\{executableName}", null);
            if (registeredPath is not null && File.Exists(registeredPath.Trim('"'))) return registeredPath.Trim('"');
        }
        return null;
    }

    private static string? ReadRegistryValue(RegistryKey registryRoot, string keyPath, string? valueName)
    {
        try
        {
            using var registryKey = registryRoot.OpenSubKey(keyPath);
            return registryKey?.GetValue(valueName) is string { Length: > 0 } registryValue ? registryValue : null;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static IEnumerable<string> ProgramFilesPaths(string relativePath)
        => new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath)
            .Where(programFilesDirectory => !string.IsNullOrEmpty(programFilesDirectory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(programFilesDirectory => Path.Combine(programFilesDirectory, relativePath));

    private static string LocalApplicationDataPath(string relativePath)
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), relativePath);

    private static string? FirstExistingFile(IEnumerable<string> candidatePaths) => candidatePaths.FirstOrDefault(File.Exists);
}
