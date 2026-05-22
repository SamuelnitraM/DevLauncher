using System.Windows;
using System.Windows.Input;

namespace DevLauncher;

public partial class ProfileNameDialog : Window
{
    public string ProfileName { get; private set; } = string.Empty;

    public ProfileNameDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) return;
        ProfileName = NameBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Confirm_Click(sender, e);
        if (e.Key == Key.Escape) Cancel_Click(sender, e);
    }
}