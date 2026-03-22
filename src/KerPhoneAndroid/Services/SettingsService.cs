using System.Text.Json;

namespace KerPhoneAndroid.Services;

/// <summary>
/// Service de persistence des parametres SIP et de l'historique d'appels.
/// Utilise MAUI Preferences pour les champs simples et JSON serialise pour l'historique.
/// </summary>
public sealed class SettingsService
{
    private const string KeySipServer = "sip_server";
    private const string KeySipUser = "sip_user";
    private const string KeySipPassword = "sip_password";
    private const string KeySipPort = "sip_port";
    private const string KeyCallHistory = "call_history";

    public string SipServer
    {
        get => Preferences.Default.Get(KeySipServer, "");
        set => Preferences.Default.Set(KeySipServer, value);
    }

    public string SipUser
    {
        get => Preferences.Default.Get(KeySipUser, "");
        set => Preferences.Default.Set(KeySipUser, value);
    }

    public string SipPassword
    {
        get => Preferences.Default.Get(KeySipPassword, "");
        set => Preferences.Default.Set(KeySipPassword, value);
    }

    public int SipPort
    {
        get => Preferences.Default.Get(KeySipPort, 5060);
        set => Preferences.Default.Set(KeySipPort, value);
    }

    public List<CallHistoryData> LoadCallHistory()
    {
        try
        {
            var json = Preferences.Default.Get(KeyCallHistory, "");
            if (!string.IsNullOrEmpty(json))
            {
                return JsonSerializer.Deserialize<List<CallHistoryData>>(json) ?? new();
            }
        }
        catch { }

        return new();
    }

    public void SaveCallHistory(IEnumerable<CallHistoryData> history)
    {
        try
        {
            var list = history.Take(10).ToList();
            var json = JsonSerializer.Serialize(list);
            Preferences.Default.Set(KeyCallHistory, json);
        }
        catch { }
    }
}

public class CallHistoryData
{
    public string Number { get; set; } = "";
    public string Direction { get; set; } = "";
    public DateTime Time { get; set; }
}
