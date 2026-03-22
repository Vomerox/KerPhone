using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace KerPhoneAndroid.Services;

/// <summary>
/// Service SIP utilisant SIPSorcery (pure C#).
/// Gere l'enregistrement, les appels sortants/entrants, mute, hold, hangup et DTMF.
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
    private uint _rtpTimestamp;

    private string _server = "";
    private string _username = "";
    private string _password = "";
    private int _port = 5060;

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

            // Ecouter les appels entrants
            _sipTransport.SIPTransportRequestReceived += OnSIPRequestReceived;

            // Demarrer l'enregistrement
            _regAgent = new SIPRegistrationUserAgent(
                _sipTransport,
                _username,
                _password,
                _server,
                120); // expiry 120s

            _regAgent.RegistrationSuccessful += (uri, resp) =>
            {
                _isRegistered = true;
                MainThread.BeginInvokeOnMainThread(() =>
                    RegistrationStateChanged?.Invoke(true, "Connecte"));
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
                    RegistrationStateChanged?.Invoke(false, "Deconnecte"));
            };

            _regAgent.Start();

            // Attendre un peu pour voir si l'enregistrement reussit
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

    /// <summary>
    /// Desinscription du serveur SIP.
    /// </summary>
    public void Unregister()
    {
        try
        {
            _regAgent?.Stop();
        }
        catch { }

        _isRegistered = false;
        Cleanup();
    }

    /// <summary>
    /// Effectue un appel SIP sortant avec session media audio.
    /// </summary>
    public async Task<bool> MakeCallAsync(string destination)
    {
        if (_sipTransport == null || !_isRegistered) return false;

        try
        {
            _userAgent = new SIPUserAgent(_sipTransport, null);

            _userAgent.ClientCallFailed += (uac, error, response) =>
            {
                StopSilenceTimer();
                var reason = error ?? response?.ReasonPhrase ?? "Erreur inconnue";
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    CallStateChanged?.Invoke("Echec");
                    ErrorOccurred?.Invoke($"Appel echoue : {reason}");
                });
            };

            _userAgent.ClientCallAnswered += (uac, response) =>
            {
                // Demarrer l'envoi de silence RTP pour maintenir l'appel
                StartSilenceTimer();
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
                StopSilenceTimer();
                MainThread.BeginInvokeOnMainThread(() =>
                    CallStateChanged?.Invoke("Raccroche"));
            };

            // Construire l'URI de destination
            string destStr;
            if (destination.Contains("@"))
                destStr = destination.StartsWith("sip:") ? destination : $"sip:{destination}";
            else
                destStr = $"sip:{destination}@{_server}:{_port}";

            // Creer une session RTP avec les codecs audio PCMU/PCMA
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

            // Desactiver le timeout RTP (evite le raccrochage automatique)
            _rtpSession.OnTimeout += (mediaType) =>
            {
                // Ne pas fermer la session sur timeout - on gere le silence nous-memes
            };

            // Reinitialiser le compteur RTP
            _rtpTimestamp = 0;

            // Effectuer l'appel avec la session RTP
            var callResult = await _userAgent.Call(
                destStr,
                _username,
                _password,
                _rtpSession);

            if (!callResult)
            {
                StopSilenceTimer();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    CallStateChanged?.Invoke("Echec");
                    ErrorOccurred?.Invoke("Le serveur a rejete l'appel.");
                });
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            StopSilenceTimer();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                CallStateChanged?.Invoke("Echec");
                ErrorOccurred?.Invoke($"Erreur d'appel : {ex.Message}");
            });
            return false;
        }
    }

    /// <summary>
    /// Demarre un timer qui envoie des paquets RTP de silence toutes les 20ms
    /// pour maintenir le flux media actif et empecher le serveur de raccrocher.
    /// </summary>
    private void StartSilenceTimer()
    {
        StopSilenceTimer();

        // Paquet de silence PCMU (mu-law) = 160 octets de 0xFF (silence mu-law)
        // Envoye toutes les 20ms (50 paquets/seconde) = 8000 Hz / 160 samples
        _silenceTimer = new Timer(_ =>
        {
            try
            {
                if (_rtpSession != null && !_rtpSession.IsClosed && _userAgent?.IsCallActive == true)
                {
                    if (!_isMuted && !_isOnHold)
                    {
                        // Envoyer un paquet de silence PCMU (0xFF = silence en mu-law)
                        var silencePayload = new byte[160];
                        Array.Fill(silencePayload, (byte)0xFF);

                        _rtpSession.SendAudio(160, silencePayload);
                    }
                }
            }
            catch { }
        }, null, 0, 20); // Toutes les 20ms
    }

    /// <summary>
    /// Arrete le timer de silence RTP.
    /// </summary>
    private void StopSilenceTimer()
    {
        _silenceTimer?.Dispose();
        _silenceTimer = null;
    }

    /// <summary>
    /// Repond a un appel entrant.
    /// </summary>
    public async Task AnswerCallAsync()
    {
        try
        {
            if (_userAgent != null && _userAgent.IsCallActive)
            {
                StartSilenceTimer();
                MainThread.BeginInvokeOnMainThread(() =>
                    CallStateChanged?.Invoke("En ligne"));
            }
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
                ErrorOccurred?.Invoke($"Erreur reponse : {ex.Message}"));
        }
    }

    /// <summary>
    /// Raccroche l'appel en cours.
    /// </summary>
    public void Hangup()
    {
        StopSilenceTimer();

        try
        {
            if (_userAgent != null && _userAgent.IsCallActive)
            {
                _userAgent.Hangup();
            }
        }
        catch { }

        try
        {
            if (_rtpSession != null && !_rtpSession.IsClosed)
            {
                _rtpSession.Close("User hangup");
            }
        }
        catch { }

        _rtpSession = null;
        _isMuted = false;
        _isOnHold = false;
    }

    /// <summary>
    /// Bascule la mise en attente de l'appel.
    /// </summary>
    public void SetHold(bool hold)
    {
        if (_userAgent == null || !_userAgent.IsCallActive) return;

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

    /// <summary>
    /// Bascule le microphone (muet/actif).
    /// </summary>
    public void SetMute(bool mute)
    {
        _isMuted = mute;
    }

    /// <summary>
    /// Envoie des DTMF pendant un appel.
    /// </summary>
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

    public bool IsRegistered => _isRegistered;
    public bool IsCallActive => _userAgent?.IsCallActive ?? false;
    public bool IsMuted => _isMuted;
    public bool IsOnHold => _isOnHold;

    /// <summary>
    /// Traite les requetes SIP entrantes (appels entrants).
    /// </summary>
    private async Task OnSIPRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
    {
        if (sipRequest.Method == SIPMethodsEnum.INVITE)
        {
            var from = sipRequest.Header.From?.FromURI?.User ?? "Inconnu";

            _userAgent = new SIPUserAgent(_sipTransport, null);

            _userAgent.OnCallHungup += (dialogue) =>
            {
                StopSilenceTimer();
                MainThread.BeginInvokeOnMainThread(() =>
                    CallStateChanged?.Invoke("Raccroche"));
            };

            // Creer une session RTP pour l'appel entrant
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
            _rtpTimestamp = 0;

            _rtpSession.OnTimeout += (mediaType) => { };

            var uas = _userAgent.AcceptCall(sipRequest);

            MainThread.BeginInvokeOnMainThread(() =>
                IncomingCall?.Invoke(from));
        }
    }

    /// <summary>
    /// Nettoie les ressources SIP.
    /// </summary>
    private void Cleanup()
    {
        StopSilenceTimer();

        try
        {
            _regAgent?.Stop();
            _regAgent = null;

            if (_userAgent?.IsCallActive == true)
                _userAgent.Hangup();

            _userAgent = null;

            if (_rtpSession != null && !_rtpSession.IsClosed)
                _rtpSession.Close("Cleanup");
            _rtpSession = null;

            _sipTransport?.Shutdown();
            _sipTransport = null;
        }
        catch { }
    }
}
