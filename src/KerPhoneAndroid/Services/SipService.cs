using System.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using Android.Media;
using Android.OS;
using Encoding = Android.Media.Encoding;

namespace KerPhoneAndroid.Services;

/// <summary>
/// Service SIP utilisant SIPSorcery (pure C#).
/// Gere l'enregistrement, les appels sortants/entrants, mute, hold, hangup et DTMF.
/// Lecture audio via Android AudioTrack, capture micro via Android AudioRecord.
/// </summary>
public sealed class SipService
{
    private SIPTransport? _sipTransport;
    private SIPRegistrationUserAgent? _regAgent;
    private SIPUserAgent? _userAgent;
    private RTPSession? _rtpSession;
    private Timer? _silenceTimer;
    private bool _isRegistered;
    private bool _isMuted;
    private bool _isOnHold;
    private bool _isCallSetup; // true pendant toute la duree de l'appel (setup + actif)
    private SIPServerUserAgent? _pendingUas; // UAS en attente de reponse pour appel entrant

    private string _server = "";
    private string _username = "";
    private string _password = "";
    private int _port = 5060;

    // Audio Android
    private AudioTrack? _audioTrack;
    private AudioRecord? _audioRecord;
    private bool _isRecording;
    private Thread? _recordThread;
    private readonly byte[] _rtpPcmBuffer = new byte[1280]; // 640 samples PCM16 max (20ms @ 8kHz * 2 bytes)
    private bool _isSpeakerOn;
    private AudioManager? _audioManager;
    private AudioFocusRequestClass? _audioFocusRequest;

    // Sonnerie/vibration appel entrant
    private Android.Media.Ringtone? _ringtone;
    private Vibrator? _vibrator;

    /* --- Evenements --- */
    public event Action<bool, string>? RegistrationStateChanged;
    public event Action<string>? IncomingCall;
    public event Action<string>? CallStateChanged;
    public event Action<string>? ErrorOccurred;

    /// <summary>
    /// Initialise le transport SIP et demarre l'enregistrement.
    /// </summary>
    public async Task<bool> RegisterAsync(string server, string username, string password, int port = 5060)
    {
        _server = server;
        _username = username;
        _password = password;
        _port = port;

        try
        {
            Cleanup();

            _sipTransport = new SIPTransport();
            _sipTransport.SIPTransportRequestReceived += OnSIPRequestReceived;

            _regAgent = new SIPRegistrationUserAgent(
                _sipTransport,
                _username,
                _password,
                _server,
                120);

            _regAgent.RegistrationSuccessful += (uri, resp) =>
            {
                _isRegistered = true;
                MainThread.BeginInvokeOnMainThread(() =>
                    RegistrationStateChanged?.Invoke(true, "Connecté"));
            };

            _regAgent.RegistrationFailed += (uri, resp, message) =>
            {
                _isRegistered = false;
                var reason = message ?? resp?.ReasonPhrase ?? "Erreur inconnue";
                MainThread.BeginInvokeOnMainThread(() =>
                    RegistrationStateChanged?.Invoke(false, $"Echec : {reason}"));
            };

            _regAgent.RegistrationRemoved += (uri, resp) =>
            {
                _isRegistered = false;
                MainThread.BeginInvokeOnMainThread(() =>
                    RegistrationStateChanged?.Invoke(false, "Déconnecté"));
            };

            _regAgent.Start();
            await Task.Delay(3000);
            return true;
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ErrorOccurred?.Invoke($"Erreur d'enregistrement : {ex.Message}");
                RegistrationStateChanged?.Invoke(false, $"Erreur : {ex.Message}");
            });
            return false;
        }
    }

    public void Unregister()
    {
        // Raccrocher un appel en cours avant de se desenregistrer
        if (_isCallSetup || (_userAgent?.IsCallActive ?? false))
            Hangup();

        try { _regAgent?.Stop(); } catch { }
        _isRegistered = false;
        Cleanup();
    }

    /// <summary>
    /// Effectue un appel SIP sortant avec session media audio.
    /// </summary>
    public async Task<bool> MakeCallAsync(string destination)
    {
        if (_sipTransport == null || !_isRegistered)
        {
            MainThread.BeginInvokeOnMainThread(() =>
                ErrorOccurred?.Invoke("Non enregistré sur le serveur SIP."));
            return false;
        }

        try
        {
            // Nettoyer tout appel precedent
            CleanupCall();

            _userAgent = new SIPUserAgent(_sipTransport, null);
            _isCallSetup = true;

            _userAgent.ClientCallFailed += (uac, error, response) =>
            {
                var reason = error ?? response?.ReasonPhrase ?? "Erreur inconnue";
                _isCallSetup = false;
                StopAudio();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    CallStateChanged?.Invoke("Échec");
                    ErrorOccurred?.Invoke($"Appel échoué : {reason}");
                });
            };

            _userAgent.ClientCallAnswered += (uac, response) =>
            {
                // L'appel est repondu - demarrer la capture micro
                StartMicCapture();
                MainThread.BeginInvokeOnMainThread(() =>
                    CallStateChanged?.Invoke("En ligne"));
            };

            _userAgent.ClientCallRinging += (uac, response) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                    CallStateChanged?.Invoke("Sonnerie..."));
            };

            _userAgent.ClientCallTrying += (uac, response) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                    CallStateChanged?.Invoke("Appel en cours..."));
            };

            _userAgent.OnCallHungup += (dialogue) =>
            {
                _isCallSetup = false;
                StopAudio();
                MainThread.BeginInvokeOnMainThread(() =>
                    CallStateChanged?.Invoke("Raccroché"));
            };

            // URI de destination
            string destStr;
            if (destination.Contains("@"))
                destStr = destination.StartsWith("sip:") ? destination : $"sip:{destination}";
            else
                destStr = $"sip:{destination}@{_server}";

            // Session RTP avec codecs PCMU/PCMA
            _rtpSession = new RTPSession(false, false, false);
            var pcmuFormat = new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU);
            var pcmaFormat = new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA);
            var audioTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.audio,
                false,
                new List<SDPAudioVideoMediaFormat> { pcmuFormat, pcmaFormat },
                MediaStreamStatusEnum.SendRecv);
            _rtpSession.addTrack(audioTrack);
            _rtpSession.AcceptRtpFromAny = true;

            // Ecouter les paquets RTP entrants pour les jouer sur le haut-parleur
            _rtpSession.OnRtpPacketReceived += OnRtpPacketReceived;

            // Desactiver le timeout RTP
            _rtpSession.OnTimeout += (mediaType) => { };

            // Demarrer la lecture audio Android (speaker)
            StartAudioPlayback();

            // Demarrer l'envoi de silence pour maintenir le flux
            StartSilenceTimer();

            // Effectuer l'appel
            var callResult = await _userAgent.Call(
                destStr,
                _username,
                _password,
                _rtpSession);

            if (!callResult)
            {
                _isCallSetup = false;
                StopAudio();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    CallStateChanged?.Invoke("Échec");
                    ErrorOccurred?.Invoke("Le serveur a rejeté l'appel.");
                });
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _isCallSetup = false;
            StopAudio();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                CallStateChanged?.Invoke("Échec");
                ErrorOccurred?.Invoke($"Erreur d'appel : {ex.Message}");
            });
            return false;
        }
    }

    /// <summary>
    /// Raccroche l'appel en cours ou annule un appel en cours de setup.
    /// </summary>
    public void Hangup()
    {
        _isCallSetup = false;
        StopAudio();

        try
        {
            if (_userAgent != null)
            {
                if (_userAgent.IsCallActive)
                {
                    _userAgent.Hangup();
                }
                else
                {
                    // L'appel est en cours de setup (sonnerie) - annuler
                    _userAgent.Cancel();
                }
            }
        }
        catch { }

        try
        {
            if (_rtpSession != null && !_rtpSession.IsClosed)
                _rtpSession.Close("User hangup");
        }
        catch { }

        _rtpSession = null;
        _userAgent = null;
        _pendingUas = null;
        _isMuted = false;
        _isOnHold = false;
    }

    /* --- Audio Android --- */

    /// <summary>
    /// Demarre la lecture audio sur le haut-parleur Android.
    /// </summary>
    private void StartAudioPlayback()
    {
        try
        {
            StopAudioPlayback();
            RequestAudioFocus();

            int sampleRate = 8000;
            var channelConfig = ChannelOut.Mono;
            var encoding = Encoding.Pcm16bit;
            int bufferSize = AudioTrack.GetMinBufferSize(sampleRate, channelConfig, encoding);
            if (bufferSize < 4096) bufferSize = 4096;

            _audioTrack = new AudioTrack.Builder()
                .SetAudioAttributes(new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.VoiceCommunication)!
                    .SetContentType(AudioContentType.Speech)!
                    .Build()!)
                .SetAudioFormat(new Android.Media.AudioFormat.Builder()
                    .SetSampleRate(sampleRate)!
                    .SetChannelMask(channelConfig)!
                    .SetEncoding(encoding)!
                    .Build()!)
                .SetBufferSizeInBytes(bufferSize)
                .SetTransferMode(AudioTrackMode.Stream)
                .Build();

            _audioTrack.Play();
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
                ErrorOccurred?.Invoke($"Erreur audio speaker : {ex.Message}"));
        }
    }

    /// <summary>
    /// Arrete la lecture audio.
    /// </summary>
    private void StopAudioPlayback()
    {
        try
        {
            if (_audioTrack != null)
            {
                if (_audioTrack.PlayState == PlayState.Playing)
                    _audioTrack.Stop();
                _audioTrack.Release();
                _audioTrack = null;
            }
        }
        catch { }
    }

    /// <summary>
    /// Demarre la capture microphone et envoie les paquets RTP.
    /// </summary>
    private void StartMicCapture()
    {
        StopMicCapture();

        try
        {
            int sampleRate = 8000;
            var channelConfig = ChannelIn.Mono;
            var encoding = Encoding.Pcm16bit;
            int bufferSize = AudioRecord.GetMinBufferSize(sampleRate, channelConfig, encoding);
            if (bufferSize < 4096) bufferSize = 4096;

            _audioRecord = new AudioRecord(
                AudioSource.VoiceCommunication,
                sampleRate,
                channelConfig,
                encoding,
                bufferSize);

            if (_audioRecord.State != State.Initialized)
            {
                _audioRecord.Release();
                _audioRecord = null;
                return;
            }

            _audioRecord.StartRecording();
            _isRecording = true;

            // Thread de capture: lit 160 samples (20ms) et envoie en PCMU
            _recordThread = new Thread(() =>
            {
                var pcmBuffer = new short[160]; // 20ms a 8kHz
                var mulawBuffer = new byte[160];

                while (_isRecording)
                {
                    try
                    {
                        if (_audioRecord == null || _audioRecord.RecordingState != RecordState.Recording)
                            break;

                        int read = _audioRecord.Read(pcmBuffer, 0, pcmBuffer.Length);
                        if (read > 0 && !_isMuted && !_isOnHold && _rtpSession != null && !_rtpSession.IsClosed)
                        {
                            // Encoder PCM16 -> mu-law
                            for (int i = 0; i < read; i++)
                                mulawBuffer[i] = LinearToMuLaw(pcmBuffer[i]);

                            _rtpSession.SendAudio((uint)read, mulawBuffer);
                        }
                    }
                    catch { break; }
                }
            });
            _recordThread.IsBackground = true;
            _recordThread.Start();
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
                ErrorOccurred?.Invoke($"Erreur micro : {ex.Message}"));
        }
    }

    /// <summary>
    /// Arrete la capture microphone.
    /// </summary>
    private void StopMicCapture()
    {
        _isRecording = false;

        try
        {
            if (_audioRecord != null)
            {
                if (_audioRecord.RecordingState == RecordState.Recording)
                    _audioRecord.Stop();
                _audioRecord.Release();
                _audioRecord = null;
            }
        }
        catch { }

        try { _recordThread?.Join(500); } catch { }
        _recordThread = null;
    }

    /// <summary>
    /// Traite les paquets RTP recus et les joue sur le haut-parleur.
    /// </summary>
    private void OnRtpPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType,
        RTPPacket rtpPacket)
    {
        if (mediaType != SDPMediaTypesEnum.audio || _audioTrack == null || _isOnHold)
            return;

        try
        {
            var payload = rtpPacket.Payload;
            int payloadType = rtpPacket.Header.PayloadType;
            int count = Math.Min(payload.Length, _rtpPcmBuffer.Length / 2);

            // Decoder mu-law (PT 0) ou a-law (PT 8) -> PCM16 dans le buffer reutilise
            for (int i = 0; i < count; i++)
            {
                short sample = payloadType == 8
                    ? ALawToLinear(payload[i])
                    : MuLawToLinear(payload[i]);
                _rtpPcmBuffer[i * 2]     = (byte)(sample & 0xFF);
                _rtpPcmBuffer[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            if (_audioTrack?.PlayState == PlayState.Playing)
                _audioTrack.Write(_rtpPcmBuffer, 0, count * 2);
        }
        catch { }
    }

    /// <summary>
    /// Arrete tout l'audio (lecture + capture + silence).
    /// </summary>
    private void StopAudio()
    {
        StopSilenceTimer();
        StopMicCapture();
        StopAudioPlayback();
        StopIncomingRingtone();
        AbandonAudioFocus();
    }

    /// <summary>
    /// Nettoie un appel precedent sans toucher au transport SIP.
    /// </summary>
    private void CleanupCall()
    {
        StopAudio();

        if (_userAgent != null)
        {
            try { if (_userAgent.IsCallActive) _userAgent.Hangup(); } catch { }
            try { _userAgent.Cancel(); } catch { }
            _userAgent = null;
        }
        if (_rtpSession != null)
        {
            try { if (!_rtpSession.IsClosed) _rtpSession.Close("New call"); } catch { }
            _rtpSession = null;
        }

        _isMuted = false;
        _isOnHold = false;
        _isCallSetup = false;
    }

    /* --- Silence RTP --- */

    private void StartSilenceTimer()
    {
        StopSilenceTimer();

        _silenceTimer = new Timer(_ =>
        {
            try
            {
                // Envoyer du silence uniquement si le micro n'est pas actif
                if (_rtpSession != null && !_rtpSession.IsClosed && !_isRecording)
                {
                    if (!_isMuted && !_isOnHold)
                    {
                        var silencePayload = new byte[160];
                        Array.Fill(silencePayload, (byte)0xFF);
                        _rtpSession.SendAudio(160, silencePayload);
                    }
                }
            }
            catch { }
        }, null, 0, 20);
    }

    private void StopSilenceTimer()
    {
        _silenceTimer?.Dispose();
        _silenceTimer = null;
    }

    /* --- Hold / Mute / DTMF --- */

    public void SetHold(bool hold)
    {
        if (_userAgent == null) return;
        try
        {
            if (hold)
                _userAgent.PutOnHold();
            else
                _userAgent.TakeOffHold();
            _isOnHold = hold;
        }
        catch { }
    }

    public void SetMute(bool mute) => _isMuted = mute;

    /// <summary>
    /// Active/desactive le haut-parleur.
    /// </summary>
    public void SetSpeaker(bool on)
    {
        _isSpeakerOn = on;
        try
        {
            var mgr = GetAudioManager();
            if (mgr != null)
            {
                mgr.SpeakerphoneOn = on;
                mgr.Mode = on ? Mode.Normal : Mode.InCommunication;
            }
        }
        catch { }
    }

    public bool IsSpeakerOn => _isSpeakerOn;

    /// <summary>
    /// Demande le focus audio Android pour les appels VoIP.
    /// </summary>
    private void RequestAudioFocus()
    {
        try
        {
            var mgr = GetAudioManager();
            if (mgr == null) return;

            mgr.Mode = Mode.InCommunication;

            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                _audioFocusRequest = new AudioFocusRequestClass.Builder(AudioFocus.GainTransient)!
                    .SetAudioAttributes(new AudioAttributes.Builder()
                        .SetUsage(AudioUsageKind.VoiceCommunication)!
                        .SetContentType(AudioContentType.Speech)!
                        .Build()!)!
                    .Build();
                mgr.RequestAudioFocus(_audioFocusRequest);
            }
        }
        catch { }
    }

    /// <summary>
    /// Libere le focus audio Android.
    /// </summary>
    private void AbandonAudioFocus()
    {
        try
        {
            var mgr = GetAudioManager();
            if (mgr == null) return;

            if (_audioFocusRequest != null && OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                mgr.AbandonAudioFocusRequest(_audioFocusRequest);
                _audioFocusRequest = null;
            }

            mgr.Mode = Mode.Normal;
            mgr.SpeakerphoneOn = false;
            _isSpeakerOn = false;
        }
        catch { }
    }

    private AudioManager? GetAudioManager()
    {
        if (_audioManager == null)
        {
            _audioManager = Android.App.Application.Context.GetSystemService(Android.Content.Context.AudioService) as AudioManager;
        }
        return _audioManager;
    }

    /// <summary>
    /// Demarre la sonnerie et la vibration pour un appel entrant.
    /// </summary>
    public void StartIncomingRingtone()
    {
        try
        {
            // Vibration
            var vibrator = (Vibrator?)Android.App.Application.Context.GetSystemService(Android.Content.Context.VibratorService);
            if (vibrator != null && vibrator.HasVibrator)
            {
                _vibrator = vibrator;
                if (OperatingSystem.IsAndroidVersionAtLeast(26))
                {
                    // Vibration en boucle : 0ms pause, 500ms vibre, 500ms pause, 500ms vibre...
                    var effect = VibrationEffect.CreateWaveform(new long[] { 0, 500, 500, 500 }, 0);
                    _vibrator.Vibrate(effect);
                }
            }

            // Sonnerie par defaut
            var ringtoneUri = Android.Media.RingtoneManager.GetDefaultUri(RingtoneType.Ringtone);
            if (ringtoneUri != null)
            {
                _ringtone = Android.Media.RingtoneManager.GetRingtone(Android.App.Application.Context, ringtoneUri);
                _ringtone?.Play();
            }
        }
        catch { }
    }

    /// <summary>
    /// Arrete la sonnerie et la vibration.
    /// </summary>
    public void StopIncomingRingtone()
    {
        try
        {
            _vibrator?.Cancel();
            _vibrator = null;
        }
        catch { }
        try
        {
            if (_ringtone?.IsPlaying == true)
                _ringtone.Stop();
            _ringtone = null;
        }
        catch { }
    }

    public async Task SendDtmfAsync(string digits)
    {
        if (_userAgent == null || !_userAgent.IsCallActive) return;
        try
        {
            foreach (var c in digits)
            {
                if (byte.TryParse(c.ToString(), out var tone))
                    await _userAgent.SendDtmf(tone);
                else if (c == '*')
                    await _userAgent.SendDtmf(10);
                else if (c == '#')
                    await _userAgent.SendDtmf(11);
            }
        }
        catch { }
    }

    public async Task AnswerCallAsync()
    {
        try
        {
            if (_userAgent != null && _pendingUas != null && _rtpSession != null)
            {
                StartAudioPlayback();
                StartSilenceTimer();

                // Envoyer le 200 OK au correspondant via la session RTP
                var answered = await _userAgent.Answer(_pendingUas, _rtpSession);
                _pendingUas = null;

                if (answered)
                {
                    StartMicCapture();
                    MainThread.BeginInvokeOnMainThread(() =>
                        CallStateChanged?.Invoke("En ligne"));
                }
                else
                {
                    StopAudio();
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        CallStateChanged?.Invoke("Échec");
                        ErrorOccurred?.Invoke("Impossible de répondre à l'appel.");
                    });
                }
            }
        }
        catch (Exception ex)
        {
            StopAudio();
            MainThread.BeginInvokeOnMainThread(() =>
                ErrorOccurred?.Invoke($"Erreur réponse : {ex.Message}"));
        }
    }

    /* --- Proprietes --- */

    public bool IsRegistered => _isRegistered;
    public bool IsCallActive => _userAgent?.IsCallActive ?? false;
    public bool IsMuted => _isMuted;
    public bool IsOnHold => _isOnHold;

    /* --- Appels entrants --- */

    private async Task OnSIPRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
    {
        if (sipRequest.Method == SIPMethodsEnum.INVITE)
        {
            var from = sipRequest.Header.From?.FromURI?.User ?? "Inconnu";

            CleanupCall();

            _userAgent = new SIPUserAgent(_sipTransport, null);
            _isCallSetup = true;

            _userAgent.OnCallHungup += (dialogue) =>
            {
                _isCallSetup = false;
                StopAudio();
                MainThread.BeginInvokeOnMainThread(() =>
                    CallStateChanged?.Invoke("Raccroché"));
            };

            _rtpSession = new RTPSession(false, false, false);
            var pcmuFormat = new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU);
            var pcmaFormat = new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA);
            var audioTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.audio,
                false,
                new List<SDPAudioVideoMediaFormat> { pcmuFormat, pcmaFormat },
                MediaStreamStatusEnum.SendRecv);
            _rtpSession.addTrack(audioTrack);
            _rtpSession.AcceptRtpFromAny = true;
            _rtpSession.OnRtpPacketReceived += OnRtpPacketReceived;
            _rtpSession.OnTimeout += (mediaType) => { };

            _pendingUas = _userAgent.AcceptCall(sipRequest);

            MainThread.BeginInvokeOnMainThread(() =>
                IncomingCall?.Invoke(from));
        }
    }

    /* --- Cleanup --- */

    private void Cleanup()
    {
        CleanupCall();

        try
        {
            _regAgent?.Stop();
            _regAgent = null;
            _sipTransport?.Shutdown();
            _sipTransport = null;
        }
        catch { }
    }

    /* --- Codecs mu-law / a-law --- */

    private static byte LinearToMuLaw(short sample)
    {
        const int MULAW_MAX = 0x1FFF;
        const int MULAW_BIAS = 33;

        int sign = (sample >> 8) & 0x80;
        if (sign != 0) sample = (short)-sample;
        if (sample > MULAW_MAX) sample = MULAW_MAX;

        sample = (short)(sample + MULAW_BIAS);

        int exponent = 7;
        for (int expMask = 0x4000; (sample & expMask) == 0 && exponent > 0; exponent--, expMask >>= 1) { }

        int mantissa = (sample >> (exponent + 3)) & 0x0F;
        byte mulaw = (byte)(~(sign | (exponent << 4) | mantissa));
        return mulaw;
    }

    private static short MuLawToLinear(byte mulaw)
    {
        mulaw = (byte)~mulaw;
        int sign = mulaw & 0x80;
        int exponent = (mulaw >> 4) & 0x07;
        int mantissa = mulaw & 0x0F;
        int sample = ((mantissa << 3) + 0x84) << exponent;
        sample -= 0x84;
        return (short)(sign != 0 ? -sample : sample);
    }

    private static short ALawToLinear(byte alaw)
    {
        alaw ^= 0x55;
        int sign = alaw & 0x80;
        int exponent = (alaw >> 4) & 0x07;
        int mantissa = alaw & 0x0F;

        int sample;
        if (exponent == 0)
            sample = (mantissa << 4) + 8;
        else
            sample = ((mantissa << 4) + 0x108) << (exponent - 1);

        return (short)(sign != 0 ? -sample : sample);
    }
}
