using System.Text.Json;

namespace VFL.GeradorWebMToken;

internal sealed class AppSettings
{
    public decimal Similarity { get; set; } = 0.260m;
    public decimal Blend { get; set; } = 0.30m;
    public decimal Zoom { get; set; } = 100m;
    public decimal PositionX { get; set; }
    public decimal PositionY { get; set; }
    public string Quality { get; set; } = QualityPreset.Alta.ToString();
    public bool KeepAudio { get; set; } = true;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VFL", "GeradorWebMToken", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, SettingsPath, true);
        }
        catch
        {
            // As preferências não devem impedir o funcionamento da conversão.
        }
    }
}
