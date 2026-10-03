namespace DevLauncher.Services;

/// <summary>Advancement of a launch : steps done out of the total, and the step in progress.</summary>
public sealed record LaunchProgress(int CompletedStepCount, int TotalStepCount, string StepLabel)
{
    public double Percentage => TotalStepCount == 0 ? 100 : 100.0 * CompletedStepCount / TotalStepCount;
}

/// <summary>Port needed by a tool and held by another program.</summary>
public sealed record PortConflict(string ToolName, int Port, int OwnerProcessId, string OwnerProcessName);

public enum PortConflictDecision
{
    /// <summary>Stops the program holding the port, then launches.</summary>
    StopOwner,
    /// <summary>Launches anyway : the tool will probably fail to start.</summary>
    Ignore,
    CancelLaunch,
}

/// <summary>Receives the advancement of a launch and decides what to do with the port conflicts.</summary>
public interface ILaunchObserver
{
    void ReportProgress(LaunchProgress progress);

    PortConflictDecision ResolvePortConflict(PortConflict portConflict);
}
