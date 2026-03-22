using System.Text;
using System.Windows;
using System.Windows.Threading;
using SoftPhone.Interop;

namespace SoftPhone.Services;

/// <summary>
/// Service SIP de haut niveau encapsulant l'interop PJSIP natif.
/// Singleton thread-safe ; les callbacks sont dispatches vers le thread UI WPF.
/// </summary>
public sealed class SipService
{
    public static SipService Instance { get; } = new();

    private bool _initialized;
    private readonly Dispatcher _dispatcher;

    // Epingler les delegates pour eviter la collecte GC
    private PjsuaApi.OnRegStateCallback? _regCb;
    private PjsuaApi.OnIncomingCallCallback? _incomingCb;
    private PjsuaApi.OnCallStateCallback? _callStateCb;
    private PjsuaApi.OnCallMediaStateCallback? _mediaCb;

    /* ─── Evenements ─── */
    public event Action<int, int, string>? RegistrationStateChanged;
    public event Action<int, int, string, string>? IncomingCall;
    public event Action<int, int, string, int>? CallStateChanged;
    public event Action<int, int>? CallMediaStateChanged;

    private SipService()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public bool Initialize()
    {
        if (_initialized) return true;

        _regCb = OnRegState;
        _incomingCb = OnIncomingCall;
        _callStateCb = OnCallState;
        _mediaCb = OnCallMediaState;

        PjsuaApi.softphone_set_callbacks(_regCb, _incomingCb, _callStateCb, _mediaCb);

        int result = PjsuaApi.softphone_init();
        if (result != 0)
        {
            MessageBox.Show(
                $"Echec de l'initialisation du moteur SIP (erreur {result}).\n\n" +
                "Verifiez que le fichier pjsip_wrapper.dll est present dans le dossier de l'application.",
                "KerPhone - Erreur moteur SIP", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        _initialized = true;
        return true;
    }

    public void Shutdown()
    {
        if (!_initialized) return;
        PjsuaApi.softphone_hangup_all();
        PjsuaApi.softphone_destroy();
        _initialized = false;
    }

    public int Register(string server, string user, string password, int port = 5060)
    {
        EnsureInit();
        return PjsuaApi.softphone_register(server, user, password, port);
    }

    public void Unregister()
    {
        if (_initialized)
            PjsuaApi.softphone_unregister();
    }

    public int MakeCall(string destination)
    {
        EnsureInit();
        return PjsuaApi.softphone_make_call(destination);
    }

    public int AnswerCall(int callId, int code = 200)
    {
        EnsureInit();
        return PjsuaApi.softphone_answer_call(callId, code);
    }

    public int HangupCall(int callId)
    {
        EnsureInit();
        return PjsuaApi.softphone_hangup_call(callId);
    }

    public void HangupAll()
    {
        if (_initialized)
            PjsuaApi.softphone_hangup_all();
    }

    public int SetHold(int callId, bool hold)
    {
        EnsureInit();
        return PjsuaApi.softphone_set_hold(callId, hold ? 1 : 0);
    }

    public int SetMute(int callId, bool mute)
    {
        EnsureInit();
        return PjsuaApi.softphone_set_mute(callId, mute ? 1 : 0);
    }

    public int SendDtmf(int callId, string digits)
    {
        EnsureInit();
        return PjsuaApi.softphone_send_dtmf(callId, digits);
    }

    public int SetVolume(int callId, float level)
    {
        EnsureInit();
        return PjsuaApi.softphone_set_rx_level(callId, level);
    }

    public int SetMicLevel(int callId, float level)
    {
        EnsureInit();
        return PjsuaApi.softphone_set_tx_level(callId, level);
    }

    public List<AudioDevice> GetAudioDevices()
    {
        var devices = new List<AudioDevice>();
        if (!_initialized) return devices;

        int count = PjsuaApi.softphone_get_sound_device_count();
        for (int i = 0; i < count; i++)
        {
            var sb = new StringBuilder(256);
            int type = PjsuaApi.softphone_get_sound_device_name(i, sb, 256);
            if (type > 0)
            {
                devices.Add(new AudioDevice
                {
                    Index = i,
                    Name = sb.ToString(),
                    IsInput = type == 1,
                    IsOutput = type == 2
                });
            }
        }
        return devices;
    }

    public int SetAudioDevices(int captureIndex, int playbackIndex)
    {
        EnsureInit();
        return PjsuaApi.softphone_set_sound_devices(captureIndex, playbackIndex);
    }

    public int TransferCall(int callId, string destination)
    {
        EnsureInit();
        return PjsuaApi.softphone_transfer_call(callId, destination);
    }

    public (int state, int mediaStatus, int durationSec) GetCallInfo(int callId)
    {
        EnsureInit();
        PjsuaApi.softphone_get_call_info(callId, out int state, out int media, out int dur);
        return (state, media, dur);
    }

    public bool IsRegistered
    {
        get
        {
            if (!_initialized) return false;
            PjsuaApi.softphone_get_reg_status(out int reg);
            return reg == 1;
        }
    }

    /* ─── Gestionnaires de callbacks natifs ─── */

    private void OnRegState(int accId, int code, string reason)
    {
        _dispatcher.BeginInvoke(() => RegistrationStateChanged?.Invoke(accId, code, reason));
    }

    private void OnIncomingCall(int accId, int callId, string from, string to)
    {
        _dispatcher.BeginInvoke(() => IncomingCall?.Invoke(accId, callId, from, to));
    }

    private void OnCallState(int callId, int state, string stateText, int lastStatus)
    {
        _dispatcher.BeginInvoke(() => CallStateChanged?.Invoke(callId, state, stateText, lastStatus));
    }

    private void OnCallMediaState(int callId, int mediaStatus)
    {
        _dispatcher.BeginInvoke(() => CallMediaStateChanged?.Invoke(callId, mediaStatus));
    }

    private void EnsureInit()
    {
        if (!_initialized)
            throw new InvalidOperationException(
                "Le service SIP n'est pas initialise. Appelez Initialize() d'abord.");
    }
}

public class AudioDevice
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public bool IsInput { get; set; }
    public bool IsOutput { get; set; }

    public override string ToString() => Name;
}
