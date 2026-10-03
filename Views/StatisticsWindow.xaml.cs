using System.Windows;
using DevLauncher.Services;

namespace DevLauncher.Views;

/// <summary>Launch figures of every launched project, most launched first.</summary>
public partial class StatisticsWindow : Window
{
    public StatisticsWindow(IReadOnlyList<ProjectLaunchStatistics> projectStatistics)
    {
        InitializeComponent();
        StatisticsItemsControl.ItemsSource = projectStatistics;
        var totalLaunchCount = projectStatistics.Sum(statistics => statistics.LaunchCount);
        SummaryText.Text = projectStatistics.Count == 0
            ? "Aucun lancement enregistré pour l'instant."
            : $"{totalLaunchCount} lancement(s) sur {projectStatistics.Count} projet(s) — projets les plus lancés en premier";
    }
}
