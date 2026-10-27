using System.IO;
using System.Text.Json;
namespace GhostGPT.Services;
public sealed class Settings
{
    public string AudioEndpoint { get; set; } = "https://api.openai.com/v1";
    public string AudioModel { get; set; } = "";
    public string ProtectedAudioKey { get; set; } = "";
    public string ChatEndpoint { get; set; } = ConnectionPresets.WellflowEndpoint;
    public string ChatModel { get; set; } = ConnectionPresets.WellflowModel;
    public string VisionModel { get; set; } = "";
    public string ProtectedChatKey { get; set; } = "";
    public string Instruction { get; set; } = "Отвечай на русском, кратко и по существу. Код оформляй блоками Markdown.";
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    public double Width { get; set; } = 1040;
    public double Height { get; set; } = 820;
    public double Opacity { get; set; } = .97;
    public bool AlwaysOnTop { get; set; } = true;
    public bool ExcludeCapture { get; set; } = true;
    public string ToggleKey { get; set; } = "Space";
    public string AnswerKey { get; set; } = "Enter";
    public string ScreenshotKey { get; set; } = "S";
    public string PanicKey { get; set; } = "H";
}
public sealed class SettingsStore(string? directory = null)
{
    private readonly string path = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GhostGPT"), "settings.json");
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
