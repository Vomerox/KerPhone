namespace KerPhoneAndroid;

public partial class CreditsPage : ContentPage
{
    public CreditsPage()
    {
        InitializeComponent();
        // H. Version lue depuis AppInfo pour rester synchronisee avec le .csproj
        VersionLabel.Text = $"v{AppInfo.VersionString}";
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private async void OnKerBzhTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await Launcher.OpenAsync(new Uri("https://kerbzh.fr"));
        }
        catch { }
    }

    private async void OnEmailTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await Launcher.OpenAsync(new Uri("mailto:contact@kerbzh.fr"));
        }
        catch { }
    }
}
