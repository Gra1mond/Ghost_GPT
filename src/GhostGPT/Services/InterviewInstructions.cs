using System.IO;
namespace GhostGPT.Services;
public static class InterviewInstructions
{
    public static string Load(string userContext)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "INTERVIEW.md");
        try
        {
            if (!File.Exists(path)) throw new ProviderException("Не найден INTERVIEW.md рядом с приложением. Восстановите файл инструкций из сборки.");
            if (new FileInfo(path).Length > 128_000) throw new ProviderException("INTERVIEW.md слишком большой: максимум 128 КБ.");
            string text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) throw new ProviderException("INTERVIEW.md пуст. Добавьте инструкции для ответов.");
            return text + "\n\n## Уточнения пользователя из настроек\n" + userContext;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new ProviderException("Не удалось прочитать INTERVIEW.md рядом с приложением. Проверьте доступ к файлу."); }
    }
}
