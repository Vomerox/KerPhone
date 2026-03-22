using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftPhone.Interop;
using SoftPhone.Services;

namespace SoftPhone.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly SipService _sip = SipService.Instance;
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly DispatcherTimer _callTimer;
    private DateTime _callStartTime;

    /* ─── Champs du compte SIP ─── */
    [ObservableProperty] private string _sipServer = "";
    [ObservableProperty] private string _sipUser = "";
    [ObservableProperty] private string _sipPassword = "";
    [ObservableProperty] private int _sipPort = 5060;

    /* ─── Etat ─── */
    [ObservableProperty] private string _registrationStatus = "Non connecte";
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
    [ObservableProperty] private bool _showSettings = true;
    [ObservableProperty] private double _volume = 1.0;
    [ObservableProperty] private int _currentCallId = -1;
    [ObservableProperty] private string _incomingCallerInfo = "";

    /* ─── Historique ─── */
    public ObservableCollection<CallHistoryEntry> CallHistory { get; } = new();

    /* ─── Peripheriques audio ─── */
    public ObservableCollection<AudioDevice> InputDevices { get; } = new();
    public ObservableCollection<AudioDevice> OutputDevices { get; } = new();
    [ObservableProperty] private AudioDevice? _selectedInputDevice;
    [ObservableProperty] private AudioDevice? _selectedOutputDevice;

    public MainViewModel()
    {
        _callTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _callTimer.Tick += (_, _) =>
        {
            CallDuration = (DateTime.Now - _callStartTime).ToString(@"mm\:ss");
        };

        _sip.RegistrationStateChanged += OnRegistrationState;
        _sip.IncomingCall += OnIncomingCall;
        _sip.CallStateChanged += OnCallStateChanged;
        _sip.CallMediaStateChanged += OnCallMediaState;

        // Charger les parametres sauvegardes
        LoadSettings();
    }

    /* ─── Persistence ─── */

    private void LoadSettings()
    {
        _settings.Load();
        var s = _settings.Settings;

        SipServer = s.SipServer;
        SipUser = s.SipUser;
        SipPassword = s.SipPassword;
        SipPort = s.SipPort > 0 ? s.SipPort : 5060;

        // Charger l'historique (max 10)
        foreach (var h in s.CallHistory.Take(10))
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
        var s = _settings.Settings;
        s.SipServer = SipServer;
        s.SipUser = SipUser;
        s.SipPassword = SipPassword;
        s.SipPort = SipPort;

        // Sauvegarder les 10 derniers appels
        s.CallHistory = CallHistory.Take(10).Select(h => new CallHistoryData
        {
            Number = h.Number,
            Direction = h.Direction,
            Time = h.Time
        }).ToList();

        _settings.Save();
    }

    /* ─── Commandes ─── */

    [RelayCommand]
    private void Connect()
    {
        if (string.IsNullOrWhiteSpace(SipServer) || string.IsNullOrWhiteSpace(SipUser))
        {
            StatusMessage = "Veuillez renseigner le serveur et l'identifiant.";
            return;
        }

        if (!_sip.Initialize())
        {
            StatusMessage = "Echec de l'initialisation du moteur SIP.";
            return;
        }

        _isUnregistering = false;
        RegistrationStatus = "Connexion en cours...";
        RegistrationColor = "#FFA500";

        // Sauvegarder les identifiants des la tentative de connexion
        SaveSettings();

        int result = _sip.Register(SipServer, SipUser, SipPassword, SipPort);
        if (result != 0)
        {
            RegistrationStatus = $"Echec d'enregistrement ({result})";
            RegistrationColor = "#FF4444";
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        _isUnregistering = true;

        // Forcer l'etat immediatement AVANT l'appel natif
        IsRegistered = false;
        RegistrationStatus = "Deconnecte";
        RegistrationColor = "#888888";
        StatusMessage = "Deconnecte du serveur SIP.";

        try
        {
            _sip.Unregister();
        }
        catch { }
    }

    [RelayCommand]
    private void Call()
    {
        if (!IsRegistered || string.IsNullOrWhiteSpace(DialNumber)) return;

        IsConnecting = true;
        CallStatus = "Appel en cours...";
        RemoteParty = DialNumber;

        int callId = _sip.MakeCall(DialNumber);
        if (callId >= 0)
        {
            CurrentCallId = callId;
            IsInCall = true;
            ShowSettings = false;
            AddHistory(DialNumber, "Sortant");
        }
        else
        {
            StatusMessage = $"Echec de l'appel (erreur {callId})";
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private void Hangup()
    {
        if (CurrentCallId >= 0)
        {
            _sip.HangupCall(CurrentCallId);
        }
        ResetCallState();
    }

    [RelayCommand]
    private void AnswerIncoming()
    {
        if (CurrentCallId >= 0)
        {
            _sip.AnswerCall(CurrentCallId, 200);
            IsIncomingCall = false;
            IsInCall = true;
            CallStatus = "En ligne";
        }
    }

    [RelayCommand]
    private void RejectIncoming()
    {
        if (CurrentCallId >= 0)
        {
            _sip.HangupCall(CurrentCallId);
        }
        IsIncomingCall = false;
        ResetCallState();
    }

    [RelayCommand]
    private void ToggleHold()
    {
        if (CurrentCallId < 0) return;
        IsOnHold = !IsOnHold;
        _sip.SetHold(CurrentCallId, IsOnHold);
        CallStatus = IsOnHold ? "En attente" : "En ligne";
    }

    [RelayCommand]
    private void ToggleMute()
    {
        if (CurrentCallId < 0) return;
        IsMuted = !IsMuted;
        _sip.SetMute(CurrentCallId, IsMuted);
    }

    [RelayCommand]
    private void DialPadPress(string? digit)
    {
        if (string.IsNullOrEmpty(digit)) return;

        if (IsInCall && CurrentCallId >= 0)
        {
            _sip.SendDtmf(CurrentCallId, digit);
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
    private void ToggleSettings()
    {
        ShowSettings = !ShowSettings;
    }

    [RelayCommand]
    private void ShowCredits()
    {
        var credits = new CreditsWindow
        {
            Owner = Application.Current.MainWindow
        };
        credits.ShowDialog();
    }

    [RelayCommand]
    private void RefreshAudioDevices()
    {
        InputDevices.Clear();
        OutputDevices.Clear();

        foreach (var dev in _sip.GetAudioDevices())
        {
            if (dev.IsInput) InputDevices.Add(dev);
            if (dev.IsOutput) OutputDevices.Add(dev);
        }
    }

    [RelayCommand]
    private void ApplyAudioDevices()
    {
        int cap = SelectedInputDevice?.Index ?? -1;
        int play = SelectedOutputDevice?.Index ?? -1;
        if (cap >= 0 && play >= 0)
            _sip.SetAudioDevices(cap, play);
    }

    partial void OnVolumeChanged(double value)
    {
        if (CurrentCallId >= 0)
            _sip.SetVolume(CurrentCallId, (float)value);
    }

    /* ─── Gestionnaires d'evenements SIP ─── */

    private void OnRegistrationState(int accId, int code, string reason)
    {
        // Si on est en cours de desinscription, ignorer tout callback
        if (_isUnregistering)
        {
            _isUnregistering = false;
            // Confirmer l'etat deconnecte
            IsRegistered = false;
            RegistrationStatus = "Deconnecte";
            RegistrationColor = "#888888";
            return;
        }

        if (code == 200)
        {
            IsRegistered = true;
            RegistrationStatus = "Connecte";
            RegistrationColor = "#44CC44";
            StatusMessage = $"Connecte en tant que {SipUser}@{SipServer}";
            RefreshAudioDevices();
        }
        else
        {
            IsRegistered = false;
            RegistrationStatus = $"Echec : {code} {reason}";
            RegistrationColor = "#FF4444";
            StatusMessage = $"Echec d'enregistrement : {reason}";
        }
    }

    private void OnIncomingCall(int accId, int callId, string from, string to)
    {
        CurrentCallId = callId;
        RemoteParty = from;
        IncomingCallerInfo = from;
        IsIncomingCall = true;
        ShowSettings = false;
        CallStatus = "Appel entrant...";
        AddHistory(from, "Entrant");
    }

    private void OnCallStateChanged(int callId, int state, string stateText, int lastStatus)
    {
        if (callId != CurrentCallId) return;

        switch (state)
        {
            case PjsuaApi.PJSIP_INV_STATE_CONFIRMED:
                IsInCall = true;
                IsConnecting = false;
                CallStatus = "En ligne";
                _callStartTime = DateTime.Now;
                _callTimer.Start();
                break;

            case PjsuaApi.PJSIP_INV_STATE_DISCONNECTED:
                _callTimer.Stop();
                ResetCallState();
                StatusMessage = $"Appel termine (code {lastStatus})";
                break;

            case PjsuaApi.PJSIP_INV_STATE_EARLY:
                CallStatus = "Sonnerie...";
                break;

            case PjsuaApi.PJSIP_INV_STATE_CALLING:
                CallStatus = "Appel en cours...";
                break;

            case PjsuaApi.PJSIP_INV_STATE_CONNECTING:
                CallStatus = "Connexion...";
                break;

            default:
                CallStatus = stateText;
                break;
        }
    }

    private void OnCallMediaState(int callId, int mediaStatus)
    {
        // Etat media gere dans le code natif (connexion audio automatique)
    }

    /* ─── Utilitaires ─── */

    private void ResetCallState()
    {
        IsInCall = false;
        IsIncomingCall = false;
        IsConnecting = false;
        IsOnHold = false;
        IsMuted = false;
        CurrentCallId = -1;
        CallStatus = "";
        CallDuration = "00:00";
        RemoteParty = "";
        IncomingCallerInfo = "";
    }

    private void AddHistory(string number, string direction)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            CallHistory.Insert(0, new CallHistoryEntry
            {
                Number = number,
                Direction = direction,
                Time = DateTime.Now
            });

            // Garder max 10 entrees
            while (CallHistory.Count > 10)
                CallHistory.RemoveAt(CallHistory.Count - 1);

            // Sauvegarder immediatement
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
