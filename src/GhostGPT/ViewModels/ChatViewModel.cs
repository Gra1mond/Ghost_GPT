using System.Collections.ObjectModel;
using GhostGPT.Services;
namespace GhostGPT.ViewModels;
public sealed class ChatMessage(string role, string text) : Observable
{
    public string Role { get; } = role;
    private string text = text;
    public string Text { get => text; set => Set(ref text, value); }
}
public partial class MainViewModel
{
    public ObservableCollection<ChatMessage> Messages { get; } = [];
    private readonly List<ChatTurn> history = [];
    private CancellationTokenSource? requestCancellation;
    private bool busy;
    public bool Busy { get => busy; private set => Set(ref busy, value); }
    public void Cancel() => requestCancellation?.Cancel();
    public void ClearChat() { if (Busy) return; Messages.Clear(); history.Clear(); }
    public void RestoreChat(IEnumerable<ChatTurn> turns)
    {
        if (Busy) return;
        ClearChat();
        foreach (var turn in turns.TakeLast(100)) { Messages.Add(new(turn.Role, turn.Text)); history.Add(turn with { ImageDataUrl = null }); }
    }
    public async Task SendAsync(IChatProvider provider, ProviderOptions options, string instruction, string prompt, string? image = null, IReadOnlyList<ChatTurn>? context = null)
    {
        if (Busy || string.IsNullOrWhiteSpace(prompt)) return;
        CompatibleProvider.Validate(options);
        Busy = true; requestCancellation = new(TimeSpan.FromMinutes(3));
        var user = new ChatTurn("user", prompt, image);
        Messages.Add(new("user", image is null ? prompt : prompt + "\n[Прикреплён скриншот]"));
        var answer = new ChatMessage("assistant", ""); Messages.Add(answer);
        var buffer = new System.Text.StringBuilder();
        var paint = System.Diagnostics.Stopwatch.StartNew();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        double firstToken = 0;
        while (Messages.Count > 100) Messages.RemoveAt(0);
        try
        {
            var turns = new List<ChatTurn> { new("system", instruction) };
            if (context is not null) turns.AddRange(context);
            else turns.AddRange(LimitHistory(history, 24000));
            turns.Add(user);
            Status = "Модель отвечает…";
            await foreach (var chunk in provider.StreamAsync(options, turns, requestCancellation.Token))
            {
                if (buffer.Length + chunk.Length > 100000) throw new ProviderException("Ответ превысил лимит отображения (100 000 символов).");
                if (buffer.Length == 0) firstToken = elapsed.Elapsed.TotalSeconds;
                buffer.Append(chunk);
                if (paint.ElapsedMilliseconds >= 45 || answer.Text.Length == 0) { answer.Text = buffer.ToString(); paint.Restart(); }
            }
            answer.Text = buffer.ToString();
            if (string.IsNullOrWhiteSpace(answer.Text)) throw new ProviderException("Модель вернула пустой ответ. Проверьте совместимость API.");
            if (context is null) { history.Add(user with { ImageDataUrl = null }); history.Add(new("assistant", answer.Text)); history.RemoveRange(0, Math.Max(0, history.Count - 40)); }
            Status = $"Ответ готов · первый фрагмент {firstToken:0.0} с · всего {elapsed.Elapsed.TotalSeconds:0.0} с";
        }
        catch (OperationCanceledException) { Status = "Запрос остановлен или истёк тайм-аут"; if (answer.Text.Length == 0) answer.Text = "Ответ отменён."; }
        catch (Exception ex) { Status = FriendlyError(ex); if (answer.Text.Length == 0) answer.Text = Status; }
        finally { if (buffer.Length > 0) answer.Text = buffer.ToString(); requestCancellation.Dispose(); requestCancellation = null; Busy = false; }
    }
    public static IEnumerable<ChatTurn> LimitHistory(IEnumerable<ChatTurn> turns, int budget)
    {
        var result = new List<ChatTurn>();
        foreach (var turn in turns.Reverse()) { if (turn.Text.Length > budget) break; result.Add(turn); budget -= turn.Text.Length; }
        result.Reverse(); while (result.Count > 0 && result[0].Role != "user") result.RemoveAt(0); return result;
    }
    public static string FriendlyError(Exception ex) => ex is ProviderException ? ex.Message : ex is System.Net.Http.HttpRequestException ? "Не удалось подключиться. Проверьте сеть и адрес API." : "Не удалось обработать запрос. Проверьте настройки и совместимость провайдера.";
}
