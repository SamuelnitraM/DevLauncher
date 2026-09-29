using System.Windows;

namespace DevLauncher.Views;

/// <summary>Asks for a profile name, used both to create and to rename a profile.</summary>
public partial class ProfileNameDialog : Window
{
    public string ProfileName { get; private set; } = string.Empty;

    public ProfileNameDialog(string dialogTitle, string confirmLabel, string initialProfileName)
    {
        InitializeComponent();
        Title = dialogTitle;
        ConfirmButton.Content = confirmLabel;
        NameBox.Text = initialProfileName;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) return;
        ProfileName = NameBox.Text.Trim();
        DialogResult = true;
    }
}
