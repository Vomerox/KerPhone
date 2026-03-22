namespace KerPhoneAndroid;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(nameof(CreditsPage), typeof(CreditsPage));
    }
}
