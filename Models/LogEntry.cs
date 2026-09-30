namespace DevLauncher.Models;

/// <summary>Timestamped line of the launch log.</summary>
public record LogEntry(string Text, bool IsError);
