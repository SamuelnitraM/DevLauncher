using CommunityToolkit.Mvvm.ComponentModel;

namespace DevLauncher.ViewModels;

/// <summary>Running indicator of a service shown in the header.</summary>
public partial class ServiceIndicatorViewModel : ObservableObject
{
    public ServiceIndicatorViewModel(string serviceName)
    {
        ServiceName = serviceName;
    }

    public string ServiceName { get; }

    [ObservableProperty]
    private bool _isRunning;
}
