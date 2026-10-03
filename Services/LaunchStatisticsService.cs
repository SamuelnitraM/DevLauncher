using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevLauncher.Services;

/// <summary>Launch figures of a project.</summary>
public sealed class ProjectLaunchStatistics
{
    public string ProjectPath { get; set; } = string.Empty;
    public int LaunchCount { get; set; }
    public DateTime LastLaunchTime { get; set; }
    public double LastDurationSeconds { get; set; }
    public double TotalDurationSeconds { get; set; }

    [JsonIgnore]
    public string ProjectName => Path.GetFileName(Path.TrimEndingDirectorySeparator(ProjectPath));
    [JsonIgnore]
    public double AverageDurationSeconds => LaunchCount == 0 ? 0 : TotalDurationSeconds / LaunchCount;
}

/// <summary>
/// Counts the launches of each project and measures how long they take, from the click to the end of the last step.
/// </summary>
public class LaunchStatisticsService
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _statisticsFilePath;

    public LaunchStatisticsService() : this(StoragePaths.LaunchStatisticsFilePath)
    {
    }

    public LaunchStatisticsService(string statisticsFilePath)
    {
        _statisticsFilePath = statisticsFilePath;
    }

    /// <summary>Returns the figures of every launched project, most launched first.</summary>
    public List<ProjectLaunchStatistics> GetStatistics()
        => ReadStatistics()
            .OrderByDescending(projectStatistics => projectStatistics.LaunchCount)
            .ThenByDescending(projectStatistics => projectStatistics.LastLaunchTime)
            .ToList();

    /// <summary>Adds a completed launch to the figures of its project.</summary>
    public void RecordLaunch(string projectPath, DateTime launchTime, TimeSpan launchDuration)
    {
        var statistics = ReadStatistics();
        var projectStatistics = statistics.FirstOrDefault(existingStatistics => PathComparer.AreSame(existingStatistics.ProjectPath, projectPath));
        if (projectStatistics is null)
        {
            projectStatistics = new ProjectLaunchStatistics { ProjectPath = projectPath };
            statistics.Add(projectStatistics);
        }
        projectStatistics.LaunchCount++;
        projectStatistics.LastLaunchTime = launchTime;
        projectStatistics.LastDurationSeconds = Math.Round(launchDuration.TotalSeconds, 1);
        projectStatistics.TotalDurationSeconds = Math.Round(projectStatistics.TotalDurationSeconds + launchDuration.TotalSeconds, 1);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statisticsFilePath)!);
            File.WriteAllText(_statisticsFilePath, JsonSerializer.Serialize(statistics, _jsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Statistics are a convenience : a failed write keeps the previous figures.
        }
    }

    private List<ProjectLaunchStatistics> ReadStatistics()
    {
        if (!File.Exists(_statisticsFilePath)) return new List<ProjectLaunchStatistics>();
        try
        {
            return JsonSerializer.Deserialize<List<ProjectLaunchStatistics>>(File.ReadAllText(_statisticsFilePath), _jsonOptions)?
                .Where(projectStatistics => !string.IsNullOrWhiteSpace(projectStatistics.ProjectPath))
                .ToList() ?? new List<ProjectLaunchStatistics>();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<ProjectLaunchStatistics>();
        }
    }
}
