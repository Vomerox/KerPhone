using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KerPhoneAndroid.Services;

namespace KerPhoneAndroid.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SipService _sip;
    private readonly SettingsService _settings;
    private IDispatcherTimer? _callTimer;
    private DateTime _callStartTime;

    /* --- Champs du compte SIP --- */
    [ObservableProperty] private string _sipServer = "";
    [ObservableProperty] private string _sipUser = "";
    [ObservableProperty] private string _sipPassword = "";
    [ObservableProperty] private string _sipPort = "5060";

    /* --- Etat --- */
    [ObservableProperty] private string _registrationStatus = "Non connecté";
    [ObservableProperty] private string _registrationColor = "#888888";
    [ObservableProperty] private bool _isRegistered;
    private bool _isUnregistering;
    [ObservableProperty] private bool _isInCall;
    [ObservableProperty] private bool _isIncomingCall;
    [ObservableProperty] private bool _isOnHold;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private string _dialNumber = "";
    [ObservableProperty] private string _callStatus = "";
    [ObservableProperty] private string _callDuration = "00:00";
    [ObservableProperty] private string _remoteParty = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _incomingCallerInfo = "";

    /* --- Proprietes calculees pour la visibilite --- */
    [ObservableProperty] private bool _showLoginPanel = true;
    [ObservableProperty] private bool _showDialPad;
    [ObservableProperty] private bool _showActiveCall;
    [ObservableProperty] private bool _showIncomingCall;

    /* --- Historique --- */
    public ObservableCollection<CallHistoryEntry> CallHistory { get; } = new();

    public MainViewModel(SipService sip, SettingsService settings)
    {
        _sip = sip;
        _settings = settings;

        _sip.RegistrationStateChanged += OnRegistrationState;
        _sip.IncomingCall += OnIncomingCall;
        _sip.CallStateChanged += OnCallStateChanged;
        _sip.ErrorOccurred += OnError;

        LoadSettings();
    }

    /// <summary>
    /// Initialise le timer d'appel (doit etre appele depuis la page avec le Dispatcher).
    /// </summary>
    public void InitializeTimer(IDispatcher dispatcher)
    {
        _callTimer = dispatcher.CreateTimer();
        _callTimer.Interval = TimeSpan.FromSeconds(1);
        _callTimer.Tick += (_, _) =>
        {
            CallDuration = (DateTime.Now - _callStartTime).ToString(@"mm\:ss");
        };
    }

    /* --- Persistence --- */

    private void LoadSettings()
    {
        SipServer = _settings.SipServer;
        SipUser = _settings.SipUser;
        SipPassword = _settings.SipPassword;
        var port = _settings.SipPort;
        SipPort = port > 0 ? port.ToString() : "5060";

        foreach (var h in _settings.LoadCallHistory().Take(10))
        {
            CallHistory.Add(new CallHistoryEntry
            {
                Number = h.Number,
                Direction = h.Direction,
                Time = h.Time
            });
        }
    }

    private void SaveSettings()
    {
        _settings.SipServer = SipServer;
        _settings.SipUser = SipUser;
        _settings.SipPassword = SipPassword;
        _settings.SipPort = int.TryParse(SipPort, out var p) ? p : 5060;

        _settings.SaveCallHistory(CallHistory.Take(10).Select(h => new CallHistoryData
        {
            Number = h.Number,
            Direction = h.Direction,
            Time = h.Time
        }));
    }

    private void UpdatePanelVisibility()
    {
        ShowLoginPanel = !IsRegistered && !IsInCall && !IsIncomingCall;
        ShowDialPad = IsRegistered && !IsInCall && !IsIncomingCall;
        ShowActiveCall = IsInCall;
        ShowIncomingCall = IsIncomingCall;
    }

    /* --- Commandes --- */

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(SipServer) || string.IsNullOrWhiteSpace(SipUser))
        {
            StatusMessage = "Veuillez renseigner le serveur et l'identifiant.";
            return;
        }

        _isUnregistering = false;
        IsConnecting = true;
        RegistrationStatus = "Connexion en cours...";
        RegistrationColor = "#FFA500";

        SaveSettings();

        int port = int.TryParse(SipPort, out var p) ? p : 5060;
        var result = await _sip.RegisterAsync(SipServer, SipUser, SipPassword, port);

        if (!result)
        {
            RegistrationStatus = "Échec de connexion";
            RegistrationColor = "#FF4444";
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        _isUnregistering = true;

        IsRegistered = false;
        RegistrationStatus = "Déconnecté";
        RegistrationColor = "#888888";
        StatusMessage = "Déconnecté du serveur SIP.";

        try
        {
            _sip.Unregister();
        }
        catch { }

        IsConnecting = false;
        UpdatePanelVisibility();
    }

    [RelayCommand]
    private async Task CallAsync()
    {
        if (!IsRegistered || string.IsNullOrWhiteSpace(DialNumber))
        {
            if (!IsRegistered)
                StatusMessage = "Non connecté au serveur SIP.";
            else
                StatusMessage = "Veuillez entrer un numéro.";
            return;
        }

        try
        {
            IsConnecting = true;
            IsInCall = true;
            CallStatus = "Appel en cours...";
            RemoteParty = DialNumber;
            UpdatePanelVisibility();

            var success = await _sip.MakeCallAsync(DialNumber);
            if (success)
            {
                AddHistory(DialNumber, "Sortant");
            }
            else
            {
                StatusMessage = "Échec de l'appel";
                IsConnecting = false;
                ResetCallState();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur : {ex.Message}";
            IsConnecting = false;
            ResetCallState();
        }
    }

    [RelayCommand]
    private void Hangup()
    {
        try
        {
            _sip.Hangup();
        }
        catch { }
        finally
        {
            _callTimer?.Stop();
            ResetCallState();
            StatusMessage = "Appel terminé";
        }
    }

    [RelayCommand]
    private void AnswerIncoming()
    {
        _ = _sip.AnswerCallAsync();
        IsIncomingCall = false;
        IsInCall = true;
        CallStatus = "En ligne";
        _callStartTime = DateTime.Now;
        _callTimer?.Start();
        UpdatePanelVisibility();
    }

    [RelayCommand]
    private void RejectIncoming()
    {
        _sip.Hangup();
        IsIncomingCall = false;
        ResetCallState();
    }

    [RelayCommand]
    private void ToggleHold()
    {
        if (!IsInCall) return;
        IsOnHold = !IsOnHold;
        _sip.SetHold(IsOnHold);
        CallStatus = IsOnHold ? "En attente" : "En ligne";
    }

    [RelayCommand]
    private void ToggleMute()
    {
        if (!IsInCall) return;
        IsMuted = !IsMuted;
        _sip.SetMute(IsMuted);
    }

    [RelayCommand]
    private void DialPadPress(string? digit)
    {
        if (string.IsNullOrEmpty(digit)) return;

        if (IsInCall)
        {
            _ = _sip.SendDtmfAsync(digit);
        }

        DialNumber += digit;
    }

    [RelayCommand]
    private void Backspace()
    {
        if (DialNumber.Length > 0)
            DialNumber = DialNumber[..^1];
    }

    [RelayCommand]
    private void ClearNumber()
    {
        DialNumber = "";
    }

    [RelayCommand]
    private async Task ShowCreditsAsync()
    {
        await Shell.Current.GoToAsync(nameof(CreditsPage));
    }

    [RelayCommand]
    private void DialFromHistory(CallHistoryEntry? entry)
    {
        if (entry == null || !IsRegistered) return;
        DialNumber = entry.Number;
    }

    /* --- Gestionnaires d'evenements SIP --- */

    private void OnRegistrationState(bool success, string message)
    {
        if (_isUnregistering)
        {
            _isUnregistering = false;
            IsRegistered = false;
            RegistrationStatus = "Déconnecté";
            RegistrationColor = "#888888";
            IsConnecting = false;
            UpdatePanelVisibility();
            return;
        }

        if (success)
        {
            IsRegistered = true;
            RegistrationStatus = "Connecté";
            RegistrationColor = "#44CC44";
            StatusMessage = $"Connecté en tant que {SipUser}@{SipServer}";
        }
        else
        {
            IsRegistered = false;
            RegistrationStatus = message;
            RegistrationColor = "#FF4444";
            StatusMessage = message;
        }

        IsConnecting = false;
        UpdatePanelVisibility();
    }

    private void OnIncomingCall(string from)
    {
        RemoteParty = from;
        IncomingCallerInfo = from;
        IsIncomingCall = true;
        CallStatus = "Appel entrant...";
        AddHistory(from, "Entrant");
        UpdatePanelVisibility();
    }

    private void OnError(string message)
    {
        StatusMessage = message;
    }

    private void OnCallStateChanged(string state)
    {
        CallStatus = state;

        switch (state)
        {
            case "En ligne":
                IsInCall = true;
                IsConnecting = false;
                _callStartTime = DateTime.Now;
                _callTimer?.Start();
                break;

            case "Raccroché":
            case "Échec":
                _callTimer?.Stop();
                ResetCallState();
                StatusMessage = state == "Raccroché" ? "Appel terminé" : "Échec de l'appel";
                break;

            case "Sonnerie...":
                CallStatus = "Sonnerie...";
                break;
        }

        UpdatePanelVisibility();
    }

    /* --- Utilitaires --- */

    private void ResetCallState()
    {
        IsInCall = false;
        IsIncomingCall = false;
        IsConnecting = false;
        IsOnHold = false;
        IsMuted = false;
        CallStatus = "";
        CallDuration = "00:00";
        RemoteParty = "";
        IncomingCallerInfo = "";
        UpdatePanelVisibility();
    }

    private void AddHistory(string number, string direction)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CallHistory.Insert(0, new CallHistoryEntry
            {
                Number = number,
                Direction = direction,
                Time = DateTime.Now
            });

            while (CallHistory.Count > 10)
                CallHistory.RemoveAt(CallHistory.Count - 1);

            SaveSettings();
        });
    }
}

public class CallHistoryEntry
{
    public string Number { get; set; } = "";
    public string Direction { get; set; } = "";
    public DateTime Time { get; set; }
    public string Display => $"{Direction} : {Number}";
    public string TimeDisplay => Time.ToString("HH:mm");
}
