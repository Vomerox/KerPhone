using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using SoftPhone.Services;
using SoftPhone.ViewModels;

namespace SoftPhone;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    // Tailles de fenetre
    private const double LOGIN_WIDTH = 400;
    private const double LOGIN_HEIGHT = 460;
    private const double PHONE_WIDTH = 460;
    private const double PHONE_HEIGHT = 740;

    // API Windows pour barre de titre sombre
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Appliquer le mode sombre a la barre de titre Windows
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int value = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));

                int captionColor = 0x26140B;
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            }
        }
        catch { }

        // Charger le mot de passe sauvegarde dans le PasswordBox
        var savedPassword = SettingsService.Instance.Settings.SipPassword;
        if (!string.IsNullOrEmpty(savedPassword))
        {
            PasswordField.Password = savedPassword;
        }

        // Taille initiale = mode connexion
        AdaptWindowSize(false);

        // Ecouter les changements de IsRegistered pour adapter la taille
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsRegistered))
        {
            AdaptWindowSize(ViewModel.IsRegistered);
        }
    }

    private void AdaptWindowSize(bool isConnected)
    {
        if (isConnected)
        {
            Width = PHONE_WIDTH;
            Height = PHONE_HEIGHT;
            MinWidth = 420;
            MinHeight = 620;
        }
        else
        {
            Width = LOGIN_WIDTH;
            Height = LOGIN_HEIGHT;
            MinWidth = 360;
            MinHeight = 400;
        }

        // Recentrer la fenetre
        if (WindowState == WindowState.Normal)
        {
            var screen = SystemParameters.WorkArea;
            Left = (screen.Width - Width) / 2;
            Top = (screen.Height - Height) / 2;
        }
    }

    private void PasswordField_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            ViewModel.SipPassword = pb.Password;
    }

    /// <summary>
    /// Touche Entree sur le champ de numero = lancer l'appel
    /// </summary>
    private void DialInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.CallCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.ClearNumberCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Touche Entree sur les champs de connexion = se connecter
    /// </summary>
    private void LoginField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.ConnectCommand.Execute(null);
            e.Handled = true;
        }
    }
}
