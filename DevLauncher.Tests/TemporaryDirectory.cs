using System.IO;

namespace DevLauncher.Tests;

/// <summary>Folder created for a test and deleted with it.</summary>
public sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "DevLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public string Combine(params string[] pathParts) => Path.Combine(new[] { DirectoryPath }.Concat(pathParts).ToArray());

    public void Dispose()
    {
        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
            // A file still opened by the system is left in the temporary folder.
        }
    }
}
