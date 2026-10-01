using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace DevLauncher.Services.Assistants;

/// <summary>
/// Finds an installed application by its name in the Start menu applications (shell:AppsFolder), which lists
/// classic installations as well as packaged ones (Microsoft Store, MSIX). Results are cached for the session.
/// </summary>
public static class InstalledApplicationLocator
{
    private const string AppsFolderPrefix = @"shell:AppsFolder\";

    private static readonly ConcurrentDictionary<string, string?> _targetsByApplicationName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the shell:AppsFolder target of the first application named like one of the given names, or null.</summary>
    public static string? FindStartMenuApplication(IReadOnlyCollection<string> applicationNames)
    {
        foreach (var applicationName in applicationNames)
        {
            var applicationTarget = _targetsByApplicationName.GetOrAdd(applicationName, SearchAppsFolder);
            if (applicationTarget is not null) return applicationTarget;
        }
        return null;
    }

    /// <summary>Lists the names and targets of the Start menu applications. Used for diagnostics.</summary>
    public static IReadOnlyList<(string Name, string Target)> ListStartMenuApplications()
    {
        var startMenuApplications = new List<(string Name, string Target)>();
        EnumerateAppsFolder((applicationName, applicationIdentifier) =>
        {
            startMenuApplications.Add((applicationName, AppsFolderPrefix + applicationIdentifier));
            return false;
        });
        return startMenuApplications;
    }

    /// <summary>Forgets the cached results, so that an application installed meanwhile is found.</summary>
    public static void ClearCache() => _targetsByApplicationName.Clear();

    private static string? SearchAppsFolder(string searchedApplicationName)
    {
        string? applicationTarget = null;
        EnumerateAppsFolder((applicationName, applicationIdentifier) =>
        {
            if (!string.Equals(applicationName, searchedApplicationName, StringComparison.OrdinalIgnoreCase)) return false;
            applicationTarget = AppsFolderPrefix + applicationIdentifier;
            return true;
        });
        return applicationTarget;
    }

    /// <summary>Walks the applications of shell:AppsFolder until the visitor returns true. Never throws.</summary>
    private static void EnumerateAppsFolder(Func<string, string, bool> visitApplication)
    {
        try
        {
            var shellApplicationType = Type.GetTypeFromProgID("Shell.Application");
            if (shellApplicationType is null) return;
            dynamic shellApplication = Activator.CreateInstance(shellApplicationType)!;
            dynamic? appsFolder = shellApplication.NameSpace("shell:AppsFolder");
            if (appsFolder is null) return;
            dynamic appsFolderItems = appsFolder.Items();
            int applicationCount = appsFolderItems.Count;
            for (var applicationIndex = 0; applicationIndex < applicationCount; applicationIndex++)
            {
                dynamic application = appsFolderItems.Item(applicationIndex);
                string applicationName = application.Name;
                string applicationIdentifier = application.Path;
                if (!string.IsNullOrWhiteSpace(applicationIdentifier) && visitApplication(applicationName, applicationIdentifier)) return;
            }
        }
        catch (Exception exception) when (exception is COMException or RuntimeBinderException or InvalidCastException or NotSupportedException)
        {
            // The Start menu cannot be read : the application is reported as not found.
        }
    }
}
