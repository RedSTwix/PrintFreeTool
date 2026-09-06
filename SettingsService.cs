using System.IO;
using System.Text.Json;

namespace PrintFreeTool;

internal static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PrintFreeTool",
        "settings.json");

    public static HotkeyShortcut LoadShortcut()
    {
        AppSettings settings = Load();
        return settings.Shortcut?.IsValid == true ? settings.Shortcut : HotkeyShortcut.Default;
    }

    public static PrintScreenSettings LoadPrintScreenSettings()
    {
        AppSettings settings = Load();
        return settings.PrintScreen ?? PrintScreenSettings.Default;
    }

    public static void SaveShortcut(HotkeyShortcut shortcut)
    {
        if (!shortcut.IsValid)
        {
            throw new ArgumentException("O atalho informado não é válido.", nameof(shortcut));
        }

        string? directory = Path.GetDirectoryName(SettingsPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        AppSettings current = Load();
        Save(new AppSettings(shortcut, current.PrintScreen ?? PrintScreenSettings.Default));
    }

    public static void SavePrintScreenSettings(PrintScreenSettings printScreen)
    {
        AppSettings current = Load();
        HotkeyShortcut shortcut = current.Shortcut?.IsValid == true ? current.Shortcut : HotkeyShortcut.Default;
        Save(new AppSettings(shortcut, printScreen));
    }

    private static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings(HotkeyShortcut.Default, PrintScreenSettings.Default);
            }

            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                   ?? new AppSettings(HotkeyShortcut.Default, PrintScreenSettings.Default);
        }
        catch
        {
            return new AppSettings(HotkeyShortcut.Default, PrintScreenSettings.Default);
        }
    }

    private static void Save(AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(SettingsPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    private sealed record AppSettings(HotkeyShortcut? Shortcut, PrintScreenSettings? PrintScreen);
}
