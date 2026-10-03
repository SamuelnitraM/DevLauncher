using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevLauncher.Services;

/// <summary>
/// Exports the settings, profiles, recent projects and favorites into a single file, and imports such a file
/// on another machine. The import merges : the imported files replace the files of the same name, the others are kept.
/// </summary>
public sealed class DataPortabilityService
{
    private const string ExportFormatName = "devlauncher-export";
    private const int ExportFormatVersion = 1;
    private const string ProfilesFolderName = "Profiles";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _dataDirectory;

    public DataPortabilityService() : this(StoragePaths.DataDirectory)
    {
    }

    public DataPortabilityService(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    /// <summary>Writes every data file (JSON files of the data directory and of its Profiles folder) into the export file. Returns the number of exported files.</summary>
    public int Export(string exportFilePath)
    {
        var exportedFiles = new JsonObject();
        foreach (var dataFilePath in EnumerateDataFiles())
        {
            var relativePath = Path.GetRelativePath(_dataDirectory, dataFilePath).Replace(Path.DirectorySeparatorChar, '/');
            try
            {
                exportedFiles[relativePath] = JsonNode.Parse(File.ReadAllText(dataFilePath));
            }
            catch (JsonException)
            {
                // A corrupted data file is not worth exporting.
            }
        }
        var exportDocument = new JsonObject
        {
            ["format"] = ExportFormatName,
            ["version"] = ExportFormatVersion,
            ["exportedAt"] = DateTime.Now.ToString("O"),
            ["files"] = exportedFiles,
        };
        File.WriteAllText(exportFilePath, exportDocument.ToJsonString(_jsonOptions));
        return exportedFiles.Count;
    }

    /// <summary>Writes the files of an export into the data directory. Returns the number of imported files. Throws InvalidDataException for a file that is not an export.</summary>
    public int Import(string exportFilePath)
    {
        JsonObject exportDocument;
        try
        {
            exportDocument = JsonNode.Parse(File.ReadAllText(exportFilePath)) as JsonObject ?? throw new InvalidDataException("Le fichier n'est pas un export DevLauncher.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Le fichier n'est pas un export DevLauncher (JSON invalide).", exception);
        }
        var isExportFormat = exportDocument["format"] is JsonValue formatValue && formatValue.TryGetValue<string>(out var formatName) && formatName == ExportFormatName;
        if (!isExportFormat || exportDocument["files"] is not JsonObject exportedFiles)
            throw new InvalidDataException("Le fichier n'est pas un export DevLauncher.");
        var filesToWrite = exportedFiles
            .Where(exportedFile => exportedFile.Value is not null)
            .Select(exportedFile => (DestinationPath: ResolveDestinationPath(exportedFile.Key), Content: exportedFile.Value!.ToJsonString(_jsonOptions)))
            .ToList();
        if (filesToWrite.Any(fileToWrite => fileToWrite.DestinationPath is null))
            throw new InvalidDataException("L'export contient un chemin de fichier non autorisé.");
        Directory.CreateDirectory(Path.Combine(_dataDirectory, ProfilesFolderName));
        foreach (var (destinationPath, content) in filesToWrite) File.WriteAllText(destinationPath!, content);
        return filesToWrite.Count;
    }

    private IEnumerable<string> EnumerateDataFiles()
    {
        var rootDataFiles = Directory.Exists(_dataDirectory) ? Directory.EnumerateFiles(_dataDirectory, "*.json") : Enumerable.Empty<string>();
        var profilesDirectory = Path.Combine(_dataDirectory, ProfilesFolderName);
        var profileFiles = Directory.Exists(profilesDirectory) ? Directory.EnumerateFiles(profilesDirectory, "*.json") : Enumerable.Empty<string>();
        return rootDataFiles.Concat(profileFiles).OrderBy(filePath => filePath, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Accepts only « name.json » or « Profiles/name.json » with a plain file name, so that an import never writes outside the data directory.</summary>
    private string? ResolveDestinationPath(string relativePath)
    {
        var pathParts = relativePath.Split('/', '\\');
        var fileName = pathParts[^1];
        var isFileNameSafe = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && fileName.Length > ".json".Length
            && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && fileName != "..";
        if (!isFileNameSafe) return null;
        return pathParts.Length switch
        {
            1 => Path.Combine(_dataDirectory, fileName),
            2 when pathParts[0].Equals(ProfilesFolderName, StringComparison.OrdinalIgnoreCase) => Path.Combine(_dataDirectory, ProfilesFolderName, fileName),
            _ => null,
        };
    }
}
