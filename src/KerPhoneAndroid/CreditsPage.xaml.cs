namespace KerPhoneAndroid;

public partial class CreditsPage : ContentPage
{
    public CreditsPage()
    {
        InitializeComponent();
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
