using System.Net;
using System.Net.Http;
using System.Text;
using GhostGPT.Services;
using GhostGPT.ViewModels;
namespace GhostGPT.Checks;
internal static class Program
{
    private static int count;
    [STAThread]
    private static int Main()
    {
        try { Run().GetAwaiter().GetResult(); Smoke(); Console.WriteLine($"PASS: {count} checks"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    private static async Task Run()
    {
        var options = new ProviderOptions("https://example.com/v1", "test-not-a-secret", "test-model");
        Check(CompatibleProvider.Endpoint(options.Endpoint, "chat/completions").AbsolutePath == "/v1/chat/completions", "endpoint preserves v1");
        foreach (var address in new[] { "http://example.com/v1", "https://user:pass@example.com/v1", "https://example.com?key=x" })
        { try { CompatibleProvider.Endpoint(address, "models"); throw new Exception("accepted invalid address"); } catch (ProviderException) { count++; } }
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            var body = await request.Content!.ReadAsStringAsync(token);
            Check(body.Contains("test-model") && request.Headers.Authorization?.Parameter == options.Key, "request model and bearer key");
            return new(HttpStatusCode.OK) { Content = new StringContent(": keepalive\n\ndata: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"Привет\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"!\"}}]}\n\ndata: [DONE]\n", Encoding.UTF8, "text/event-stream") };
        }));
        var answer = new StringBuilder();
        await foreach (var part in new CompatibleProvider(client).StreamAsync(options, [new("user", "Hi")], default)) answer.Append(part);
        Check(answer.ToString() == "Привет!", "SSE streams UTF-8 deltas");
        using var bad = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests))));
        try { await foreach (var _ in new CompatibleProvider(bad).StreamAsync(options, [], default)) { } throw new Exception("429 ignored"); }
        catch (ProviderException ex) { Check(ex.Message.Contains("429"), "rate limit error"); }
        using var slow = new HttpClient(new Handler(async (_, token) => { await Task.Delay(10000, token); return new(HttpStatusCode.OK); }));
        using var cancellation = new CancellationTokenSource(30);
        try { await foreach (var _ in new CompatibleProvider(slow).StreamAsync(options, [], cancellation.Token)) { } throw new Exception("cancel ignored"); }
        catch (OperationCanceledException) { Check(true, "cancellation interrupts HTTP"); }
        using var truncated = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n") })));
        try { await foreach (var _ in new CompatibleProvider(truncated).StreamAsync(options, [], default)) { } throw new Exception("truncation ignored"); }
        catch (ProviderException) { Check(true, "truncated stream reported"); }
        var history = MainViewModel.LimitHistory([new("user", "old"), new("assistant", "answer"), new("user", "new"), new("assistant", "yes")], 6).ToArray();
        Check(history.Length == 2 && history[0].Text == "new", "context bounded by recent complete turn");
        var secret = SecretStore.Protect("sample"); Check(secret != "sample" && SecretStore.Unprotect(secret) == "sample", "DPAPI roundtrip");
        using var queue = new AudioQueue(1);
        var accepted = new AudioChunk("mic", DateTimeOffset.UtcNow, [1, 2]); var dropped = new AudioChunk("mic", DateTimeOffset.UtcNow, [3, 4]);
        Check(queue.TryAdd(accepted) && !queue.TryAdd(dropped), "bounded audio queue rejects overflow");
        Check(dropped.Data.All(b => b == 0), "overflow audio erased");
        queue.Dispose(); Check(accepted.Data.All(b => b == 0), "stop drains and erases queued audio");
        using var audioClient = new HttpClient(new Handler(async (request, token) =>
        {
            var body = await request.Content!.ReadAsStringAsync(token);
            Check(request.RequestUri!.AbsolutePath.EndsWith("audio/transcriptions") && body.Contains("speech.wav") && body.Contains("audio/wav"), "multipart audio contract");
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"text\":\" речь \"}") };
        }));
        Check(await new TranscriptionProvider(audioClient).TranscribeAsync(options, [1,2,3], default) == "речь", "transcription response");
        using var visionClient = new HttpClient(new Handler(async (request, token) =>
        {
            var body = await request.Content!.ReadAsStringAsync(token);
            Check(body.Contains("image_url") && body.Contains("data:image/png;base64,abc"), "vision image attachment contract");
            return new(HttpStatusCode.OK) { Content = new StringContent("data: [DONE]\n") };
        }));
        await foreach (var _ in new CompatibleProvider(visionClient).StreamAsync(options, [new("user", "image", "data:image/png;base64,abc")], default)) { }
        var context = new ConversationContext();
        for (int i = 0; i < 10; i++) context.Add("mic", new string('a', 3000));
        var snapshot = context.Snapshot();
        Check(snapshot.Recent.Length < 12100 && snapshot.Older.Length < 24100, "conversation bounded");
        context.Add("mic", new string('b', 8000));
        context.CommitSummary(snapshot, "summary");
        Check(context.Snapshot().Older.Length > 0 && context.Summary == "summary", "new transcript survives concurrent summary");
        context.Clear(); Check(!context.HasContent, "new session clears context");
    }
    private static void Smoke()
    {
        var app = new GhostGPT.App(); app.InitializeComponent();
        var window = new GhostGPT.MainWindow();
        Check(window.Content is not null, "WPF view constructs with settings and chat controls");
        var content = (System.Windows.FrameworkElement)window.Content!;
        content.Measure(new(740, 790)); content.Arrange(new(0, 0, 740, 790)); content.UpdateLayout();
        Check(content.ActualWidth == 740, "WPF layout");
        window.Close();
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }
}
