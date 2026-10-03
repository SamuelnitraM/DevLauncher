using System.IO;
using DevLauncher.Models;
using DevLauncher.Services;
using DevLauncher.Services.Stacks;
using DevLauncher.Services.Tools;
using Xunit;

namespace DevLauncher.Tests;

public class DatabaseConnectionReaderTests
{
    [Fact]
    public void DoctrineUrlIsParsed()
    {
        var databaseConnection = DatabaseConnectionReader.ParseDatabaseUrl("mysql://app:%21ChangeMe%21@127.0.0.1:3307/boutique?serverVersion=8.0.32&charset=utf8mb4");
        Assert.Equal(new DatabaseConnection("mysql", "127.0.0.1", 3307, "app", "!ChangeMe!", "boutique"), databaseConnection);
        Assert.True(databaseConnection!.IsMySql);
        Assert.Equal(new DatabaseConnection("mysql", "localhost", 3306, "root", "", "site"), DatabaseConnectionReader.ParseDatabaseUrl("mysql://root@localhost/site"));
        Assert.Equal("postgresql", DatabaseConnectionReader.ParseDatabaseUrl("postgresql://app:pass@127.0.0.1:5432/app")?.Engine);
        Assert.False(DatabaseConnectionReader.ParseDatabaseUrl("sqlite:///%kernel.project_dir%/var/data.db")?.IsMySql);
        Assert.Null(DatabaseConnectionReader.ParseDatabaseUrl("pas une url"));
    }

    [Fact]
    public void LocalEnvironmentFileOverridesTheDefaultOne()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllLines(temporaryDirectory.Combine(".env"), new[]
        {
            "# Base par défaut",
            "APP_ENV=dev",
            "DATABASE_URL=\"mysql://app:secret@127.0.0.1:3306/app?serverVersion=8\"",
        });
        File.WriteAllLines(temporaryDirectory.Combine(".env.local"), new[] { "export DATABASE_URL='mysql://root:@127.0.0.1:3306/templatesite' # base locale" });
        var databaseConnection = DatabaseConnectionReader.ReadFromProject(temporaryDirectory.DirectoryPath);
        Assert.Equal("templatesite", databaseConnection?.DatabaseName);
        Assert.Equal("root", databaseConnection?.User);
        Assert.Equal(string.Empty, databaseConnection?.Password);
    }

    [Fact]
    public void LaravelVariablesAreRead()
    {
        var environmentVariables = DatabaseConnectionReader.ParseEnvironmentFile(new[]
        {
            "DB_CONNECTION=mysql", "DB_HOST=127.0.0.1", "DB_PORT=3306", "DB_DATABASE=boutique # commentaire", "DB_USERNAME=root", "DB_PASSWORD=",
        }).ToDictionary(variable => variable.Name, variable => variable.Value);
        Assert.Equal(new DatabaseConnection("mysql", "127.0.0.1", 3306, "root", "", "boutique"), DatabaseConnectionReader.FromEnvironment(environmentVariables));
        Assert.Null(DatabaseConnectionReader.FromEnvironment(new Dictionary<string, string> { ["APP_ENV"] = "dev" }));
    }

    [Theory]
    [InlineData("boutique", true)]
    [InlineData("site_2024-dev", true)]
    [InlineData("app`; DROP DATABASE x; --", false)]
    [InlineData("base avec espace", false)]
    [InlineData("", false)]
    public void OnlyPlainDatabaseNamesAreAccepted(string databaseName, bool isExpectedSafe) => Assert.Equal(isExpectedSafe, DatabaseTool.IsSafeDatabaseName(databaseName));

    [Fact]
    public void DumpsAreFoundInTheUsualFolders()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Directory.CreateDirectory(temporaryDirectory.Combine("database"));
        Directory.CreateDirectory(temporaryDirectory.Combine("src"));
        File.WriteAllText(temporaryDirectory.Combine("structure.sql"), "");
        File.WriteAllText(temporaryDirectory.Combine("database", "seed.sql"), "");
        File.WriteAllText(temporaryDirectory.Combine("src", "ignored.sql"), "");
        Assert.Equal(new[] { "structure.sql", Path.Combine("database", "seed.sql") }, DatabaseTool.FindDumpFiles(temporaryDirectory.DirectoryPath));
    }
}

public class VirtualHostServiceTests
{
    [Theory]
    [InlineData(@"C:\xampp\htdocs\templateSite", "templatesite.test")]
    [InlineData(@"C:\xampp\htdocs\Mon Site Été", "mon-site-ete.test")]
    [InlineData(@"C:\xampp\htdocs\___", "projet.test")]
    public void HostNameComesFromTheFolderName(string projectPath, string expectedHostName)
        => Assert.Equal(expectedHostName, VirtualHostService.BuildHostName(projectPath.Replace('\\', Path.DirectorySeparatorChar)));

    [Fact]
    public void VirtualHostIsAddedToApacheAndHostsThenRemoved()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var xamppDirectory = temporaryDirectory.Combine("xampp");
        Directory.CreateDirectory(Path.Combine(xamppDirectory, "apache", "conf", "extra"));
        var mainConfigurationPath = Path.Combine(xamppDirectory, "apache", "conf", "httpd.conf");
        File.WriteAllLines(mainConfigurationPath, new[] { "Listen 80" });
        var hostsFilePath = temporaryDirectory.Combine("hosts");
        File.WriteAllLines(hostsFilePath, new[] { "# Copyright Microsoft", "127.0.0.1 intranet.local" });
        var projectPath = temporaryDirectory.Combine("htdocs", "boutique");
        Directory.CreateDirectory(Path.Combine(projectPath, "public"));
        var otherProjectPath = temporaryDirectory.Combine("clients", "boutique");
        Directory.CreateDirectory(otherProjectPath);
        var virtualHostService = new VirtualHostService(temporaryDirectory.Combine("data", "virtual-hosts.json"), xamppDirectory, hostsFilePath);
        Assert.Equal("boutique.test", virtualHostService.Add(projectPath, 80));
        Assert.Equal("boutique-2.test", virtualHostService.Add(otherProjectPath, 80));
        Assert.Equal("boutique.test", virtualHostService.FindHostName(projectPath));
        var includedConfiguration = File.ReadAllText(Path.Combine(xamppDirectory, "apache", "conf", "extra", "devlauncher-vhosts.conf"));
        Assert.Contains("ServerName localhost", includedConfiguration);
        Assert.True(includedConfiguration.IndexOf("ServerName localhost", StringComparison.Ordinal) < includedConfiguration.IndexOf("ServerName boutique.test", StringComparison.Ordinal));
        Assert.Contains($"DocumentRoot \"{Path.Combine(projectPath, "public").Replace('\\', '/')}\"", includedConfiguration);
        Assert.Single(File.ReadAllLines(mainConfigurationPath), configurationLine => configurationLine.Contains("devlauncher-vhosts.conf"));
        virtualHostService.Add(projectPath, 80);
        Assert.Single(File.ReadAllLines(mainConfigurationPath), configurationLine => configurationLine.Contains("devlauncher-vhosts.conf"));
        var hostsLines = File.ReadAllLines(hostsFilePath);
        Assert.Contains("127.0.0.1 intranet.local", hostsLines);
        Assert.Equal(4, hostsLines.Count(hostsLine => hostsLine.EndsWith("# DevLauncher")));
        virtualHostService.Remove(projectPath, 80);
        virtualHostService.Remove(otherProjectPath, 80);
        Assert.Null(virtualHostService.FindHostName(projectPath));
        Assert.Equal(new[] { "# Copyright Microsoft", "127.0.0.1 intranet.local" }, File.ReadAllLines(hostsFilePath));
    }

    [Fact]
    public void VirtualHostUrlFollowsTheApachePort()
    {
        Assert.Equal("http://boutique.test/", ProjectUrlResolver.BuildVirtualHostUrl("boutique.test", 80));
        Assert.Equal("http://boutique.test:8080/", ProjectUrlResolver.BuildVirtualHostUrl("boutique.test", 8080));
    }
}

public class DockerComposeDetectionTests
{
    [Fact]
    public void ComposeFileEnablesDockerInTheDefaultProfile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(temporaryDirectory.Combine("docker-compose.yml"), "services: {}");
        var projectDetection = new ProjectScanner().DetectProject(temporaryDirectory.DirectoryPath);
        Assert.True(projectDetection.HasDockerCompose);
        Assert.Equal(temporaryDirectory.Combine("docker-compose.yml"), ProjectScanner.FindComposeFile(temporaryDirectory.DirectoryPath));
        Assert.True(ProjectProfile.CreateDefault(projectDetection).IsToolEnabled(ToolIds.DockerCompose));
        Assert.False(ProjectProfile.CreateDefault(projectDetection with { HasDockerCompose = false }).IsToolEnabled(ToolIds.DockerCompose));
    }
}
