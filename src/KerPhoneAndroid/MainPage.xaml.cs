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

        // Touche Entree sur le champ mot de passe -> connexion
        PasswordEntry.Completed += (_, _) =>
        {
            if (_viewModel.ConnectCommand.CanExecute(null))
                _viewModel.ConnectCommand.Execute(null);
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
    {
        if (value is int count)
            return count > 0;
        return !string.IsNullOrEmpty(value as string);
    }

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
/// Behavior qui anime une touche du clavier en retour visuel au toucher.
/// </summary>
public class PressAnimationBehavior : Behavior<Border>
{
    private Border? _border;

    protected override void OnAttachedTo(Border bindable)
    {
        base.OnAttachedTo(bindable);
        _border = bindable;
        foreach (var tap in bindable.GestureRecognizers.OfType<TapGestureRecognizer>())
            tap.Tapped += OnTapped;
    }

    protected override void OnDetachingFrom(Border bindable)
    {
        base.OnDetachingFrom(bindable);
        foreach (var tap in bindable.GestureRecognizers.OfType<TapGestureRecognizer>())
            tap.Tapped -= OnTapped;
        _border = null;
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_border == null) return;
        await _border.ScaleTo(0.88, 60, Easing.CubicOut);
        await _border.ScaleTo(1.0, 120, Easing.SpringOut);
    }
}

/// <summary>
/// Convertisseur pour le texte du bouton Muet.
/// </summary>
public class MuteTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? "\U0001F507 Muet" : "\U0001F50A Micro";

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}
