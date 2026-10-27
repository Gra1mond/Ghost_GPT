using System.IO;
using System.Text.Json;
namespace GhostGPT.Services;
public sealed class Settings
{
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    public double Width { get; set; } = 740;
    public double Height { get; set; } = 790;
    public double Opacity { get; set; } = .97;
    public bool AlwaysOnTop { get; set; } = true;
    public bool ExcludeCapture { get; set; } = true;
    public string ToggleKey { get; set; } = "Space";
    public string AnswerKey { get; set; } = "Enter";
    public string ScreenshotKey { get; set; } = "S";
    public string PanicKey { get; set; } = "H";
}
public sealed class SettingsStore
{
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GhostGPT", "settings.json");
    public string? LoadWarning { get; private set; }
    public Settings Load()
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { LoadWarning = "Не удалось прочитать настройки. Используются значения по умолчанию."; return new(); }
    }
    public void Save(Settings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings));
        File.Move(path + ".tmp", path, true);
    }
}
