using System.Windows;
using SoftPhone.Services;

namespace SoftPhone;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"Une erreur inattendue est survenue :\n{args.Exception.Message}",
                "KerPhone - Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SipService.Instance.Shutdown();
        base.OnExit(e);
    }
}
