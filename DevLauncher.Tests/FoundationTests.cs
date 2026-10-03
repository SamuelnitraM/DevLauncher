using System.IO;
using System.Text.Json.Nodes;
using DevLauncher.Services;
using Xunit;

namespace DevLauncher.Tests;

public class PersistentLogWriterTests
{
    [Fact]
    public void LinesAreWrittenInTheFileOfTheDay()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var launchLog = new LaunchLog();
        var isDetailedLogEnabled = false;
        var currentTime = new DateTime(2026, 10, 3, 9, 15, 0);
        using (new PersistentLogWriter(temporaryDirectory.DirectoryPath, launchLog, () => isDetailedLogEnabled, () => currentTime))
        {
            launchLog.Info("Lancement : templateSite");
            launchLog.Detail("commande cachée");
            launchLog.ServiceOutput("Symfony · site", "sortie cachée", false);
            isDetailedLogEnabled = true;
            launchLog.Detail("commande visible");
            launchLog.ServiceOutput("Symfony · site", "Listening on 8000", false);
            currentTime = currentTime.AddDays(1);
            launchLog.Error("Le lendemain");
        }
        var firstDayLog = File.ReadAllText(PersistentLogWriter.GetLogFilePath(temporaryDirectory.DirectoryPath, new DateOnly(2026, 10, 3)));
        Assert.Contains("09:15:00.000 INF Lancement : templateSite", firstDayLog);
        Assert.DoesNotContain("cachée", firstDayLog);
        Assert.Contains("DBG commande visible", firstDayLog);
        Assert.Contains("OUT [Symfony · site] Listening on 8000", firstDayLog);
        var secondDayLog = File.ReadAllText(PersistentLogWriter.GetLogFilePath(temporaryDirectory.DirectoryPath, new DateOnly(2026, 10, 4)));
        Assert.Contains("ERR Le lendemain", secondDayLog);
    }

    [Fact]
    public void OnlyTheMostRecentFilesAreKept()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        for (var dayIndex = 1; dayIndex <= 14; dayIndex++)
            File.WriteAllText(PersistentLogWriter.GetLogFilePath(temporaryDirectory.DirectoryPath, new DateOnly(2026, 9, dayIndex)), "x");
        File.WriteAllText(temporaryDirectory.Combine("autre.txt"), "x");
        PersistentLogWriter.DeleteOldLogFiles(temporaryDirectory.DirectoryPath, 10);
        var remainingLogFiles = Directory.GetFiles(temporaryDirectory.DirectoryPath, "devlauncher-*.log").Select(Path.GetFileName).OrderBy(fileName => fileName).ToList();
        Assert.Equal(10, remainingLogFiles.Count);
        Assert.Equal("devlauncher-2026-09-05.log", remainingLogFiles[0]);
        Assert.True(File.Exists(temporaryDirectory.Combine("autre.txt")));
    }
}

public class DataPortabilityServiceTests
{
    [Fact]
    public void ExportedDataIsImportedOnAnotherMachine()
    {
        using var sourceDataDirectory = new TemporaryDirectory();
        using var destinationDataDirectory = new TemporaryDirectory();
        using var exportDirectory = new TemporaryDirectory();
        File.WriteAllText(sourceDataDirectory.Combine("settings.json"), """{"symfonyPort":8001}""");
        File.WriteAllText(sourceDataDirectory.Combine("recent-projects.json"), """["C:\\xampp\\htdocs\\site"]""");
        Directory.CreateDirectory(sourceDataDirectory.Combine("Profiles"));
        File.WriteAllText(sourceDataDirectory.Combine("Profiles", "site.json"), """[{"name":"Défaut","tools":{}}]""");
        File.WriteAllText(sourceDataDirectory.Combine("Profiles", "broken.json"), "{ not json");
        Directory.CreateDirectory(destinationDataDirectory.Combine("Profiles"));
        File.WriteAllText(destinationDataDirectory.Combine("Profiles", "other.json"), "[]");
        File.WriteAllText(destinationDataDirectory.Combine("settings.json"), """{"symfonyPort":9000}""");
        var exportFilePath = exportDirectory.Combine("export.json");
        Assert.Equal(3, new DataPortabilityService(sourceDataDirectory.DirectoryPath).Export(exportFilePath));
        Assert.Equal(3, new DataPortabilityService(destinationDataDirectory.DirectoryPath).Import(exportFilePath));
        Assert.Equal(8001, JsonNode.Parse(File.ReadAllText(destinationDataDirectory.Combine("settings.json")))!["symfonyPort"]!.GetValue<int>());
        Assert.Contains("Défaut", File.ReadAllText(destinationDataDirectory.Combine("Profiles", "site.json")));
        Assert.True(File.Exists(destinationDataDirectory.Combine("Profiles", "other.json")));
        Assert.False(File.Exists(destinationDataDirectory.Combine("Profiles", "broken.json")));
    }

    [Theory]
    [InlineData("""{"format":"autre","files":{}}""")]
    [InlineData("""{"format":12,"files":{}}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""pas du json""")]
    [InlineData("""{"format":"devlauncher-export","files":{"../evil.json":{}}}""")]
    [InlineData("""{"format":"devlauncher-export","files":{"Profiles/../../evil.json":{}}}""")]
    [InlineData("""{"format":"devlauncher-export","files":{"script.ps1":{}}}""")]
    public void InvalidOrDangerousExportsAreRejected(string exportContent)
    {
        using var dataDirectory = new TemporaryDirectory();
        var exportFilePath = dataDirectory.Combine("export.json");
        File.WriteAllText(exportFilePath, exportContent);
        Assert.Throws<InvalidDataException>(() => new DataPortabilityService(dataDirectory.Combine("data")).Import(exportFilePath));
        Assert.False(Directory.Exists(dataDirectory.Combine("data")));
    }
}

public class SettingsDetectionTests
{
    [Fact]
    public void CommandIsFoundThroughThePath()
    {
        using var firstFolder = new TemporaryDirectory();
        using var secondFolder = new TemporaryDirectory();
        File.WriteAllText(secondFolder.Combine("code.cmd"), "@echo off");
        File.WriteAllText(secondFolder.Combine("php.exe"), "");
        var pathVariable = string.Join(Path.PathSeparator, firstFolder.DirectoryPath, "\"" + secondFolder.DirectoryPath + "\"", "");
        Assert.Equal(secondFolder.Combine("code.cmd"), ExecutableLocator.FindInPath("code", pathVariable, ".exe;.cmd"));
        Assert.Equal(secondFolder.Combine("php.exe"), ExecutableLocator.FindInPath("php.exe", pathVariable, ".exe;.cmd"));
        Assert.Null(ExecutableLocator.FindInPath("symfony", pathVariable, ".exe;.cmd"));
        Assert.Null(ExecutableLocator.FindInPath("", pathVariable, ".EXE"));
    }

    [Fact]
    public void XamppInstallationIsRecognized()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var xamppDirectory = temporaryDirectory.Combine("xampp");
        Directory.CreateDirectory(Path.Combine(xamppDirectory, "apache", "bin"));
        Directory.CreateDirectory(Path.Combine(xamppDirectory, "mysql", "bin"));
        File.WriteAllText(Path.Combine(xamppDirectory, "apache", "bin", "httpd.exe"), "");
        File.WriteAllText(Path.Combine(xamppDirectory, "mysql", "bin", "my.ini"), "");
        Assert.Equal(xamppDirectory, SettingsAutoDetector.FindXamppDirectory(new[] { "", temporaryDirectory.Combine("missing"), xamppDirectory }));
        var componentPaths = SettingsAutoDetector.GetXamppComponentPaths(xamppDirectory);
        Assert.Equal(new[] { DetectableSetting.ApacheExecutable, DetectableSetting.MySqlConfiguration }, componentPaths.Keys.OrderBy(setting => setting));
        Assert.Equal(Path.Combine(xamppDirectory, "apache", "bin", "httpd.exe"), componentPaths[DetectableSetting.ApacheExecutable]);
    }

    [Fact]
    public void PathsAreValidatedByKind()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.Combine("tool.exe");
        File.WriteAllText(filePath, "");
        Assert.True(SettingsAutoDetector.IsValid(SettingPathKind.File, filePath));
        Assert.True(SettingsAutoDetector.IsValid(SettingPathKind.File, $"  \"{filePath}\" "));
        Assert.False(SettingsAutoDetector.IsValid(SettingPathKind.Folder, filePath));
        Assert.True(SettingsAutoDetector.IsValid(SettingPathKind.Folder, temporaryDirectory.DirectoryPath));
        Assert.True(SettingsAutoDetector.IsValid(SettingPathKind.Executable, filePath));
        Assert.False(SettingsAutoDetector.IsValid(SettingPathKind.Executable, "commande-introuvable-devlauncher"));
        Assert.False(SettingsAutoDetector.IsValid(SettingPathKind.File, "   "));
    }
}
