using System.IO;
using System.Text.Json;
namespace GhostGPT.Services;
public sealed record InterviewQuestion(string Id, string Topic, string Question, string Answer, string Source);
public sealed class QuestionCatalog
{
    public IReadOnlyList<InterviewQuestion> Questions { get; }
    private QuestionCatalog(List<InterviewQuestion> questions) { Questions = questions.AsReadOnly(); }
    public static QuestionCatalog Load()
    {
        using var stream = typeof(QuestionCatalog).Assembly.GetManifestResourceStream("GhostGPT.Assets.questions.json") ?? throw new InvalidDataException("База вопросов отсутствует.");
        var items = JsonSerializer.Deserialize<List<InterviewQuestion>>(stream) ?? throw new InvalidDataException("База вопросов пуста.");
        if (items.Count != 200 || items.Select(q => q.Id).Distinct().Count() != 200 || items.Any(q => string.IsNullOrWhiteSpace(q.Question) || string.IsNullOrWhiteSpace(q.Answer) || !Uri.TryCreate(q.Source, UriKind.Absolute, out var uri) || uri.Scheme != "https")) throw new InvalidDataException("Неверный формат базы вопросов.");
        return new(items);
    }
    public IReadOnlyList<InterviewQuestion> Search(string? topic, string? query)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Questions.Where(q => (string.IsNullOrEmpty(topic) || topic == "Все" || q.Topic == topic) && words.All(w => (q.Question + " " + q.Answer).Contains(w, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
    public static string Prompt(InterviewQuestion question) => "Разбери вопрос по " + question.Topic + ": " + question.Question + "\nДай понятный ответ, пример и типичные ошибки.";
}
