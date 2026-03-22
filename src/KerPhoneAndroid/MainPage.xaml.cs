using KerPhoneAndroid.ViewModels;

namespace KerPhoneAndroid;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Initialiser le timer avec le dispatcher de la page
        _viewModel.InitializeTimer(Dispatcher);

        // Demander les permissions Android au runtime
        await RequestPermissionsAsync();

        // Gerer la touche Entree sur le champ mot de passe -> connexion
        PasswordEntry.Completed += (_, _) =>
        {
            if (_viewModel.ConnectCommand.CanExecute(null))
                _viewModel.ConnectCommand.Execute(null);
        };

        // Gerer la touche Entree sur le champ numero -> appeler
        DialEntry.Completed += (_, _) =>
        {
            if (_viewModel.CallCommand.CanExecute(null))
                _viewModel.CallCommand.Execute(null);
        };
    }

    private async Task RequestPermissionsAsync()
    {
        var micStatus = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (micStatus != PermissionStatus.Granted)
        {
            micStatus = await Permissions.RequestAsync<Permissions.Microphone>();
        }

        var netStatus = await Permissions.CheckStatusAsync<Permissions.NetworkState>();
        if (netStatus != PermissionStatus.Granted)
        {
            await Permissions.RequestAsync<Permissions.NetworkState>();
        }
    }
}

/// <summary>
/// Convertisseur string vers bool (non vide = true).
/// </summary>
public class StringToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Convertisseur bool inverse.
/// </summary>
public class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : false;
}

/// <summary>
/// Convertisseur pour le texte du bouton Muet.
/// </summary>
public class MuteTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? "Son actif" : "Muet";

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Convertisseur pour le texte du bouton Attente.
/// </summary>
public class HoldTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? "Reprendre" : "Attente";

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}
