using KerPhoneAndroid.ViewModels;

namespace KerPhoneAndroid;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(nameof(CreditsPage), typeof(CreditsPage));
    }

    // I. Bloquer le bouton retour Android pendant un appel actif ou entrant
    protected override bool OnBackButtonPressed()
    {
        var vm = Handler?.MauiContext?.Services?.GetService<MainViewModel>();
        if (vm?.IsInCall == true || vm?.IsIncomingCall == true)
            return true; // Consomme l'evenement, ne quitte pas l'app
        return base.OnBackButtonPressed();
    }
}
