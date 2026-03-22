using System.IO;
using System.Text.Json;

namespace SoftPhone.Services;

/// <summary>
/// Service de persistence des parametres et de l'historique d'appels.
/// Sauvegarde dans un fichier JSON a cote de l'executable.
/// </summary>
public sealed class SettingsService
{
    public static SettingsService Instance { get; } = new();

    private static readonly string SettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "kerphone_settings.json");

    private AppSettings _settings = new();

    private SettingsService() { }

    public AppSettings Settings => _settings;

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            _settings = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_settings, options);
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}

public class AppSettings
{
    public string SipServer { get; set; } = "";
    public string SipUser { get; set; } = "";
    public string SipPassword { get; set; } = "";
    public int SipPort { get; set; } = 5060;
    public List<CallHistoryData> CallHistory { get; set; } = new();
}

public class CallHistoryData
{
    public string Number { get; set; } = "";
    public string Direction { get; set; } = "";
    public DateTime Time { get; set; }
}
