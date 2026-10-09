using System.Text.Json;

namespace BHComboTrigger;

internal enum Facing
{
    Right,
    Left
}

internal sealed record ComboDefinition(string Weapon, string Name, string Script, string Notes = "");

internal sealed class KeyMap
{
    public Keys Left { get; init; } = Keys.A;
    public Keys Right { get; init; } = Keys.D;
    public Keys AimUp { get; init; } = Keys.W;
    public Keys Jump { get; init; } = Keys.Space;
    public Keys Down { get; init; } = Keys.S;
    public Keys Light { get; init; } = Keys.J;
    public Keys Heavy { get; init; } = Keys.K;
    public Keys Throw { get; init; } = Keys.F;
    public Keys Dodge { get; init; } = Keys.LShiftKey;
}

internal sealed class AppSettings
{
    public int TriggerKey { get; set; } = (int)Keys.I;
    public int IntervalMs { get; set; } = 35;
    public string Facing { get; set; } = "Right";
    public string Weapon { get; set; } = "Axe";
    public string Combo { get; set; } = "SLight → NAir";
    public bool TriggerEnabled { get; set; } = true;
    public bool TopMost { get; set; }

    public int LeftKey { get; set; } = (int)Keys.A;
    public int RightKey { get; set; } = (int)Keys.D;
    public int AimUpKey { get; set; } = (int)Keys.W;
    public int JumpKey { get; set; } = (int)Keys.Space;
    public int DownKey { get; set; } = (int)Keys.S;
    public int LightKey { get; set; } = (int)Keys.J;
    public int HeavyKey { get; set; } = (int)Keys.K;
    public int ThrowKey { get; set; } = (int)Keys.F;
    public int DodgeKey { get; set; } = (int)Keys.LShiftKey;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BHComboTrigger",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
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
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Settings persistence should never prevent the app from closing or running.
        }
    }
}

internal sealed record KeyChoice(string Name, Keys Key)
{
    public override string ToString() => Name;
}
