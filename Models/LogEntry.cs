using DevLauncher.Services.Hosting;

namespace DevLauncher.Models;

/// <summary>Timestamped line of a log tab. Lines of the services keep the colors they were printed with.</summary>
/// <param name="Segments">Styled pieces of the message, null for a plain message.</param>
public record LogEntry(string Timestamp, string Message, bool IsError, IReadOnlyList<AnsiSegment>? Segments = null)
{
    /// <summary>Line as copied and searched : timestamp and message.</summary>
    public string Text => $"[{Timestamp}] {Message}";
}
