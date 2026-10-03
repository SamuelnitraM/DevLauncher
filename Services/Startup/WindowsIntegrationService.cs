using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace DevLauncher.Services.Startup;

/// <summary>
/// Registers DevLauncher in Windows : the devlauncher:// protocol, the « Lancer avec DevLauncher » entry of the folders
/// in the Explorer, and the start at logon. The start at logon goes through a scheduled task with the highest privileges :
/// Windows never starts an application requiring the administrator rights from the Run key.
/// </summary>
public sealed class WindowsIntegrationService
{
    public const string StartupTaskName = "DevLauncher";
    private const string ContextMenuKeyName = "DevLauncher";
    private const string ContextMenuLabel = "Lancer avec DevLauncher";

    private readonly RegistryKey _classesRoot;
    private readonly string _classesKeyPath;
    private readonly string _executablePath;

    /// <summary>Registers in the classes of the current user, for the running executable.</summary>
    public WindowsIntegrationService() : this(Registry.CurrentUser, @"Software\Classes", Environment.ProcessPath ?? "DevLauncher.exe")
    {
    }

    public WindowsIntegrationService(RegistryKey classesRoot, string classesKeyPath, string executablePath)
    {
        _classesRoot = classesRoot;
        _classesKeyPath = classesKeyPath;
        _executablePath = executablePath;
    }

    // ════════════════════════════════════════════════════════
    //  PROTOCOL AND EXPLORER MENU
    // ════════════════════════════════════════════════════════

    public static string BuildProtocolCommand(string executablePath) => $"\"{executablePath}\" \"%1\"";

    public static string BuildFolderCommand(string executablePath, string folderPlaceholder) => $"\"{executablePath}\" --project \"{folderPlaceholder}\"";

    /// <summary>True when the protocol is registered, whatever the executable it points to.</summary>
    public bool IsShellIntegrationRegistered()
    {
        using var protocolKey = _classesRoot.OpenSubKey($@"{_classesKeyPath}\{StartupCommand.ProtocolScheme}");
        return protocolKey is not null;
    }

    /// <summary>True when the protocol points to another executable : DevLauncher was moved since the registration.</summary>
    public bool IsShellIntegrationOutdated()
    {
        using var commandKey = _classesRoot.OpenSubKey($@"{_classesKeyPath}\{StartupCommand.ProtocolScheme}\shell\open\command");
        return commandKey?.GetValue(null) is string registeredCommand && registeredCommand != BuildProtocolCommand(_executablePath);
    }

    /// <summary>Registers the devlauncher:// protocol and the Explorer entries of the folders and of the folder backgrounds.</summary>
    public void RegisterShellIntegration()
    {
        var iconValue = $"\"{_executablePath}\",0";
        using (var protocolKey = _classesRoot.CreateSubKey($@"{_classesKeyPath}\{StartupCommand.ProtocolScheme}"))
        {
            protocolKey.SetValue(null, "URL:DevLauncher");
            protocolKey.SetValue("URL Protocol", string.Empty);
            using var defaultIconKey = protocolKey.CreateSubKey("DefaultIcon");
            defaultIconKey.SetValue(null, iconValue);
            using var commandKey = protocolKey.CreateSubKey(@"shell\open\command");
            commandKey.SetValue(null, BuildProtocolCommand(_executablePath));
        }
        foreach (var (shellKeyPath, folderPlaceholder) in GetFolderMenuKeys())
        {
            using var menuKey = _classesRoot.CreateSubKey($@"{_classesKeyPath}\{shellKeyPath}\{ContextMenuKeyName}");
            menuKey.SetValue(null, ContextMenuLabel);
            menuKey.SetValue("Icon", iconValue);
            using var commandKey = menuKey.CreateSubKey("command");
            commandKey.SetValue(null, BuildFolderCommand(_executablePath, folderPlaceholder));
        }
    }

    public void UnregisterShellIntegration()
    {
        _classesRoot.DeleteSubKeyTree($@"{_classesKeyPath}\{StartupCommand.ProtocolScheme}", throwOnMissingSubKey: false);
        foreach (var (shellKeyPath, _) in GetFolderMenuKeys())
            _classesRoot.DeleteSubKeyTree($@"{_classesKeyPath}\{shellKeyPath}\{ContextMenuKeyName}", throwOnMissingSubKey: false);
    }

    /// <summary>A right click on a folder passes its path as %1, a right click inside an opened folder passes it as %V.</summary>
    private static IEnumerable<(string ShellKeyPath, string FolderPlaceholder)> GetFolderMenuKeys()
        => new[] { (@"Directory\shell", "%1"), (@"Directory\Background\shell", "%V") };

    // ════════════════════════════════════════════════════════
    //  START AT LOGON
    // ════════════════════════════════════════════════════════

    public bool IsStartAtLogonEnabled() => RunScheduledTasks("/Query", "/TN", StartupTaskName) == 0;

    /// <summary>Creates the logon task of the current user, minimized, with the highest privileges, also on battery.</summary>
    public bool EnableStartAtLogon()
    {
        var taskDefinitionPath = Path.Combine(Path.GetTempPath(), $"devlauncher-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(taskDefinitionPath, BuildStartupTaskXml(_executablePath, WindowsIdentity.GetCurrent().Name), Encoding.Unicode);
            return RunScheduledTasks("/Create", "/TN", StartupTaskName, "/XML", taskDefinitionPath, "/F") == 0;
        }
        finally
        {
            File.Delete(taskDefinitionPath);
        }
    }

    public bool DisableStartAtLogon() => !IsStartAtLogonEnabled() || RunScheduledTasks("/Delete", "/TN", StartupTaskName, "/F") == 0;

    public static string BuildStartupTaskXml(string executablePath, string userName) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>Démarre DevLauncher à l'ouverture de session.</Description>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
              <UserId>{SecurityElement.Escape(userName)}</UserId>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{SecurityElement.Escape(userName)}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Enabled>true</Enabled>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{SecurityElement.Escape(executablePath)}</Command>
              <Arguments>--minimized</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    /// <summary>Runs schtasks.exe without window and returns its exit code, or -1 when it cannot run.</summary>
    private static int RunScheduledTasks(params string[] arguments)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments) processStartInfo.ArgumentList.Add(argument);
            using var scheduledTasksProcess = Process.Start(processStartInfo);
            if (scheduledTasksProcess is null) return -1;
            scheduledTasksProcess.StandardOutput.ReadToEnd();
            scheduledTasksProcess.StandardError.ReadToEnd();
            scheduledTasksProcess.WaitForExit();
            return scheduledTasksProcess.ExitCode;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return -1;
        }
    }
}
