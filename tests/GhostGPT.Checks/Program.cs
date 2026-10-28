using System.Net;
using System.Net.Http;
using System.Text;
using GhostGPT.Services;
using GhostGPT.ViewModels;
namespace GhostGPT.Checks;
internal static class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    private static int count;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--export-icon")) { GhostGPT.Controls.GhostVisual.ExportIcon("src/GhostGPT/Assets/ghost.ico"); return 0; }
        try { Run().GetAwaiter().GetResult(); if (args.Contains("--wellflow-live")) WellflowLive().GetAwaiter().GetResult(); Smoke(args.Contains("--render")); Console.WriteLine($"PASS: {count} checks"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    private static async Task WellflowLive()
    {
        var key = Environment.GetEnvironmentVariable("GHOSTGPT_TEST_API_KEY") ?? throw new Exception("Set GHOSTGPT_TEST_API_KEY for the opt-in live check.");
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var provider = new CompatibleProvider(client);
        var options = new ProviderOptions(ConnectionPresets.WellflowEndpoint, key, ConnectionPresets.WellflowModel);
        var answer = new StringBuilder();
        await foreach (var part in provider.StreamAsync(options, [new("user", "Ответь только словом OK.")], timeout.Token)) answer.Append(part);
        Check(!string.IsNullOrWhiteSpace(answer.ToString()), "live Wellflow gpt-5.6-sol streaming response");
    }
    private static async Task Run()
    {
        var options = new ProviderOptions("https://example.com/v1", "test-not-a-secret", "test-model");
        var wellflow = ConnectionPresets.Wellflow(options);
        Check(wellflow.Endpoint == "https://api.wellflow.dev/v1" && wellflow.Model == "gpt-5.6-sol" && wellflow.Key == "", "Wellflow preset clears other-provider key");
        Check(ConnectionPresets.Wellflow(wellflow with { Key = "test-key" }).Key == "test-key", "Wellflow preset preserves same-origin key");
        var mismatch = new ProviderOptions("https://api.openai.com/v1", "wf_test_placeholder", "gpt-5.6-sol");
        Check(ConnectionPresets.CorrectKnownMismatch(mismatch).Endpoint == ConnectionPresets.WellflowEndpoint, "legacy Wellflow address corrected");
        Check(ConnectionPresets.CorrectKnownMismatch(options) == options, "custom provider retained");
        try { CompatibleProvider.ValidateConnection(mismatch); throw new Exception("mismatched key allowed"); }
        catch (ProviderException) { Check(true, "Wellflow key blocked from OpenAI endpoint"); }
        using var modelsClient = new HttpClient(new Handler((request, _) =>
        {
            Check(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/v1/models", "model discovery route");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"gpt-6-sol\"},{\"id\":\"gpt-5.6-sol\"},{\"id\":\"gpt-5.6-sol\"},{\"other\":1}]}") });
        }));
        var models = await new CompatibleProvider(modelsClient).ModelsAsync(options with { Model = "" }, default);
        Check(models.SequenceEqual(new[] { "gpt-5.6-sol", "gpt-6-sol" }), "model discovery without preselected model");
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
        var archiveDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GhostGPT-checks", Guid.NewGuid().ToString("N"));
        var archive = new ChatArchive(archiveDirectory);
        var saved = new SavedChat { Title = "private test title", Folder = "Собеседования", Turns = [new("user","private test prompt"),new("assistant","reply")] };
        archive.Save([saved]);
        Check(archive.Load().Single().Turns[0].Text == "private test prompt", "chat archive encrypted roundtrip");
        Check(archive.Load().Single().Folder == "Собеседования", "chat folder survives encrypted archive reload");
        Check(!System.IO.File.ReadAllText(System.IO.Path.Combine(archiveDirectory,"chats.protected")).Contains("private test"), "archive does not store plaintext");
        archive.Save([]); Check(archive.Load().Count == 0, "deleted chats stay deleted after reload");
        var restored = new MainViewModel(); restored.RestoreChat(saved.Turns);
        Check(restored.Messages.Count == 2 && restored.Messages[1].Text == "reply", "chat switching restores messages");
        var burst = new MainViewModel(); int paints = 0;
        burst.Messages.CollectionChanged += (_, _) => { if (burst.Messages.LastOrDefault() is { Role: "assistant" } message) message.PropertyChanged += (_, _) => paints++; };
        await burst.SendAsync(new BurstProvider(), options, "", "test");
        Check(burst.Messages.Last().Text.Length == 10000 && paints < 100, "burst tokens are batched without losing final text");
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
    private static void Smoke(bool render)
    {
        var app = new GhostGPT.App(); app.InitializeComponent();
        var formatted = (System.Windows.Controls.StackPanel)GhostGPT.Controls.MarkdownView.Render("Пример **ответа** с кодом `value`.");
        var selectable = (System.Windows.Controls.RichTextBox)formatted.Children[0];
        selectable.SelectAll();
        Check(selectable.IsReadOnly && selectable.Selection.Text.Contains("ответа") && selectable.Selection.Text.Contains("value"), "formatted answer supports native text selection");
        var liveMessage = new GhostGPT.ViewModels.ChatMessage("assistant", "");
        var liveView = new GhostGPT.Controls.StreamingAnswer(liveMessage);
        var liveBody = (System.Windows.Controls.ContentControl)liveView.Children[0];
        var liveStatus = (System.Windows.Controls.TextBlock)liveView.Children[1];
        Check(liveBody.Content is null && liveStatus.Text.Contains("Ожидаю"), "streaming answer shows waiting state before first token");
        liveMessage.Text = "## Решение\n\n**Ответ**\n\n```csharp\nvar value = 1;";
        typeof(GhostGPT.Controls.StreamingAnswer).GetMethod("Refresh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(liveView, null);
        var liveBlocks = (System.Windows.Controls.StackPanel)liveBody.Content!;
        Check(liveBlocks.Children.Count == 3 && liveBlocks.Children[2] is System.Windows.Controls.Border && liveStatus.Text.Contains("поступает"), "streaming Markdown formats heading and unfinished code fence");
        var ghost = GhostGPT.Controls.GhostVisual.Create();
        GhostGPT.Controls.GhostVisual.Animate(ghost,true);
        GhostGPT.Controls.GhostVisual.Animate(ghost,false);
        Check(ghost.RenderTransform is System.Windows.Media.TransformGroup ghostTransforms && ghostTransforms.Children.All(t => !t.HasAnimatedProperties) && ghost.Source is not null, "ghost animation stops cleanly");
        var smooth = new GhostGPT.Controls.SmoothScrollViewer { Height = 200, Width = 300, Content = new System.Windows.Controls.Border { Height = 2000 } };
        smooth.Measure(new(300,200)); smooth.Arrange(new(0,0,300,200)); smooth.UpdateLayout();
        smooth.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent });
        var frame = new System.Windows.Threading.DispatcherFrame();
        var stopFrame = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        stopFrame.Tick += (_, _) => { stopFrame.Stop(); frame.Continue = false; };
        stopFrame.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
        Check(smooth.VerticalOffset > 0 && smooth.VerticalOffset <= smooth.ScrollableHeight, "smooth wheel advances within scroll bounds");
        smooth.JumpToLatest();
        Check(Math.Abs(smooth.VerticalOffset - smooth.ScrollableHeight) < 1, "jump to latest reaches bottom immediately");
        var window = new GhostGPT.MainWindow(new SettingsStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GhostGPT-checks", Guid.NewGuid().ToString("N"))));
        Check(window.Content is not null, "WPF view constructs with settings and chat controls");
        Check(((System.Windows.Controls.TextBox)window.FindName("InstructionEditor")).Text.Contains("Основной формат"), "instruction editor loads packaged INTERVIEW.md");
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        Check(hwnd != IntPtr.Zero, "native window initializes");
        Check(GetWindowDisplayAffinity(hwnd, out var affinity) && affinity == 0x11, "Windows accepts capture exclusion flag");
        var fullscreenMethod = typeof(GhostGPT.MainWindow).GetMethod("ToggleFullscreen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        double savedWidth = window.Width, savedHeight = window.Height;
        fullscreenMethod.Invoke(window,null); fullscreenMethod.Invoke(window,null);
        Check(Math.Abs(window.Width-savedWidth)<1 && Math.Abs(window.Height-savedHeight)<1, "fullscreen restores window geometry");
        var content = (System.Windows.FrameworkElement)window.Content!;
        content.Measure(new(740, 790)); content.Arrange(new(0, 0, 740, 790)); content.UpdateLayout();
        Check(content.ActualWidth == 740, "WPF layout");
        var viewModel = (MainViewModel)window.DataContext;
        viewModel.Messages.Add(new("user","History smoke question")); viewModel.Messages.Add(new("assistant","History smoke answer"));
        ((System.Windows.Controls.Button)window.FindName("NewChatButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var historyHost = (System.Windows.Controls.StackPanel)window.FindName("HistoryHost");
        Check(historyHost.Children.Count == 1 && viewModel.Messages.Count == 0, "new chat archives previous conversation");
        ((System.Windows.Controls.Button)((System.Windows.Controls.DockPanel)historyHost.Children[0]).Children[1]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Check(viewModel.Messages.Count == 2 && viewModel.Messages[1].Text == "History smoke answer", "sidebar reopens saved conversation");
        var search = (System.Windows.Controls.TextBox)window.FindName("HistorySearch");
        search.Text = "smoke answer";
        Check(((System.Windows.Controls.StackPanel)window.FindName("HistoryHost")).Children.Count == 1, "history search matches message contents");
        search.Text = "no-such-conversation-123";
        Check(((System.Windows.Controls.StackPanel)window.FindName("HistoryHost")).Children.Count == 0, "history search empty state");
        search.Clear();
        var toggle = (System.Windows.Controls.Button)window.FindName("SidebarToggle");
        toggle.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Check(((System.Windows.Controls.Border)window.FindName("Sidebar")).Visibility == System.Windows.Visibility.Collapsed, "sidebar hides explicitly");
        toggle.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Check(System.Windows.Controls.Grid.GetColumnSpan((System.Windows.UIElement)window.FindName("Sidebar")) == 1 && ((System.Windows.Controls.ColumnDefinition)window.FindName("SidebarColumn")).Width.Value == 280, "sidebar reserves its own column without overlay");
        ((System.Windows.Controls.Button)window.FindName("NewChatButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        viewModel.BackgroundTransparency = .75;
        content.UpdateLayout();
        var surface = (System.Windows.Controls.Border)window.FindName("WindowSurface");
        var color = ((System.Windows.Media.SolidColorBrush)surface.Background).Color;
        Check(window.Opacity == 1 && window.AllowsTransparency && color.A == 64 && color.R == 33, "transparency changes background alpha, not window opacity or RGB");
        var transparentFrame = new System.Windows.Media.Imaging.RenderTargetBitmap(740,790,96,96,System.Windows.Media.PixelFormats.Pbgra32); transparentFrame.Render(content);
        var pixel = new byte[4]; transparentFrame.CopyPixels(new System.Windows.Int32Rect(730,400,1,1),pixel,4,0);
        Check(pixel[3] == 64, "rendered background is genuinely translucent");
        viewModel.BackgroundOpacity = .96;
        foreach (double width in new[] { 480d, 740d, 1200d })
        {
            content.Measure(new(width, 790)); content.Arrange(new(0, 0, width, 790)); content.UpdateLayout();
            Check(content.ActualWidth == width, $"layout width {width}");
        }
        typeof(GhostGPT.MainWindow).GetField("sidebarOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, null);
        if (render)
        {
            var sampleChats = (List<SavedChat>)typeof(GhostGPT.MainWindow).GetField("savedChats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
            sampleChats.Add(new SavedChat { Title = "Асинхронность и многопоточность", Folder = "Подготовка к собеседованию", Turns = [new("user","Как работает async/await?")] });
            sampleChats.Add(new SavedChat { Title = "Архитектура сервиса", Folder = "Подготовка к собеседованию", Turns = [new("user","Разбор архитектурной задачи")] });
            typeof(GhostGPT.MainWindow).GetMethod("RefreshHistory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window,null);
            Check(((System.Windows.Controls.StackPanel)window.FindName("FolderTreeHost")).Children.Count == 1, "folder tree groups conversations");
            System.IO.Directory.CreateDirectory("artifacts/previews");
            foreach (var name in new[] { "ChatPage", "ConversationPage", "SettingsPage", "InstructionsPage" })
            {
                var navigation = (System.Windows.Controls.StackPanel)window.FindName("Navigation");
                int tab = name == "ChatPage" ? 0 : name == "ConversationPage" ? 1 : name == "SettingsPage" ? 2 : 3;
                ((System.Windows.Controls.Button)navigation.Children[tab]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                content.Measure(new(1040, 820)); content.Arrange(new(0, 0, 1040, 820)); content.UpdateLayout();
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap(1040, 820, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); image.Render(content);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var stream = System.IO.File.Create($"artifacts/previews/{name}.png"); encoder.Save(stream);
            }
            var nav = (System.Windows.Controls.StackPanel)window.FindName("Navigation");
            ((System.Windows.Controls.Button)nav.Children[0]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            viewModel.Messages.Add(new("user", "Объясни, чем асинхронный код отличается от параллельного."));
            viewModel.Messages.Add(new("assistant", "Асинхронность помогает **не блокировать ожидание**, а параллельность — выполнять несколько вычислений одновременно.\n\n### Пример\nПока приложение ждёт ответ API, интерфейс остаётся отзывчивым.\n\n```csharp\nvar reply = await provider.SendAsync(message);\nShowReply(reply);\n```\n\nОдин подход не исключает другой."));
            content.Measure(new(1040,820)); content.Arrange(new(0,0,1040,820)); content.UpdateLayout();
            var demo = new System.Windows.Media.Imaging.RenderTargetBitmap(1040,820,96,96,System.Windows.Media.PixelFormats.Pbgra32); demo.Render(content);
            var demoEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); demoEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(demo));
            using var demoStream = System.IO.File.Create("artifacts/previews/ChatExample.png"); demoEncoder.Save(demoStream);
        }
        window.Close();
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }
    private sealed class BurstProvider : IChatProvider
    {
        public async IAsyncEnumerable<string> StreamAsync(ProviderOptions options, IReadOnlyList<ChatTurn> turns, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        { await Task.CompletedTask; for (int i=0;i<10000;i++) { cancellationToken.ThrowIfCancellationRequested(); yield return "x"; } }
    }
}
