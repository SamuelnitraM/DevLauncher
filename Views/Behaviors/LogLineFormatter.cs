using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DevLauncher.Models;
using DevLauncher.Views.Shell;

namespace DevLauncher.Views.Behaviors;

/// <summary>
/// Fills a TextBlock with a log line : the timestamp, then the message, split into colored runs when the line
/// kept the colors printed by its service. Uncolored runs inherit the foreground of the TextBlock (red for errors).
/// </summary>
public static class LogLineFormatter
{
    public static readonly DependencyProperty EntryProperty = DependencyProperty.RegisterAttached(
        "Entry", typeof(LogEntry), typeof(LogLineFormatter), new PropertyMetadata(null, OnEntryChanged));

    private static readonly Dictionary<string, Brush> _brushesByColor = new(StringComparer.OrdinalIgnoreCase);

    public static LogEntry? GetEntry(DependencyObject element) => (LogEntry?)element.GetValue(EntryProperty);

    public static void SetEntry(DependencyObject element, LogEntry? value) => element.SetValue(EntryProperty, value);

    private static void OnEntryChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock textBlock) return;
        textBlock.Inlines.Clear();
        if (e.NewValue is not LogEntry logEntry) return;
        textBlock.Inlines.Add(new Run($"[{logEntry.Timestamp}] "));
        if (logEntry.Segments is null || logEntry.IsError)
        {
            textBlock.Inlines.Add(new Run(logEntry.Message));
            return;
        }
        foreach (var segment in logEntry.Segments)
        {
            var segmentRun = new Run(segment.Text);
            if (segment.ForegroundColor is not null) segmentRun.Foreground = GetBrush(segment.ForegroundColor);
            if (segment.IsBold) segmentRun.FontWeight = FontWeights.Bold;
            textBlock.Inlines.Add(segmentRun);
        }
    }

    /// <summary>
    /// Brushes are shared and frozen : a busy service prints thousands of lines with a handful of colors.
    /// On the light theme, the light colors meant for a dark terminal are darkened to stay readable.
    /// </summary>
    private static Brush GetBrush(string color)
    {
        var cacheKey = $"{(ThemeService.IsDarkThemeActive ? "dark" : "light")}{color}";
        if (_brushesByColor.TryGetValue(cacheKey, out var cachedBrush)) return cachedBrush;
        var lineColor = (Color)ColorConverter.ConvertFromString(color);
        var relativeLuminance = (0.2126 * lineColor.R + 0.7152 * lineColor.G + 0.0722 * lineColor.B) / 255;
        if (!ThemeService.IsDarkThemeActive && relativeLuminance > 0.6)
            lineColor = Color.FromRgb((byte)(lineColor.R * 0.55), (byte)(lineColor.G * 0.55), (byte)(lineColor.B * 0.55));
        var colorBrush = new SolidColorBrush(lineColor);
        colorBrush.Freeze();
        _brushesByColor[cacheKey] = colorBrush;
        return colorBrush;
    }
}
