using KerPhoneAndroid.ViewModels;

namespace KerPhoneAndroid;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private bool _passwordHandlerAttached;
    private bool _transitionsInitialized;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Initialiser le timer avec le dispatcher de la page (une seule fois)
        _viewModel.InitializeTimer(Dispatcher);

        // Demander les permissions Android au runtime
        await RequestPermissionsAsync();

        // Touche Entree sur le champ mot de passe -> connexion (une seule fois)
        if (!_passwordHandlerAttached)
        {
            PasswordEntry.Completed += OnPasswordEntryCompleted;
            _passwordHandlerAttached = true;
        }

        // Abonnement aux animations de transition (une seule fois)
        if (!_transitionsInitialized)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _transitionsInitialized = true;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        PasswordEntry.Completed -= OnPasswordEntryCompleted;
        _passwordHandlerAttached = false;
    }

    private void OnPasswordEntryCompleted(object? sender, EventArgs e)
    {
        if (_viewModel.ConnectCommand.CanExecute(null))
            _viewModel.ConnectCommand.Execute(null);
    }

    // B. Animations de transition entre panneaux
    private async void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ShowActiveCall) when _viewModel.ShowActiveCall:
                ActiveCallPanel.Opacity = 0;
                await ActiveCallPanel.FadeTo(1, 220, Easing.CubicIn);
                break;

            case nameof(MainViewModel.ShowIncomingCall) when _viewModel.ShowIncomingCall:
                IncomingCallPanel.Opacity = 0;
                await IncomingCallPanel.FadeTo(1, 200, Easing.CubicIn);
                break;

            case nameof(MainViewModel.ShowDialPad) when _viewModel.ShowDialPad:
                DialPadPanel.Opacity = 0;
                await DialPadPanel.FadeTo(1, 200, Easing.CubicIn);
                break;

            case nameof(MainViewModel.ShowLoginPanel) when _viewModel.ShowLoginPanel:
                LoginPanel.Opacity = 0;
                await LoginPanel.FadeTo(1, 200, Easing.CubicIn);
                break;
        }
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
        // D. Haptic feedback
        HapticFeedback.Default.Perform(HapticFeedbackType.Click);
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

/// <summary>
/// Convertisseur pour le label de statut du micro.
/// </summary>
public class MicStatusTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? "Micro coupé" : "Micro actif";

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}
