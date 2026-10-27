using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GhostGPT.Services;
using GhostGPT.ViewModels;
using NAudio.CoreAudioApi;
namespace GhostGPT;
public partial class MainWindow
{
    private TextBox audioEndpoint = null!, audioModel = null!;
    private PasswordBox audioKey = null!;
    private ComboBox microphones = null!, outputs = null!;
    private Button recordButton = null!;
    private readonly TextBlock recordingStatus = new() { Text = "●  Запись остановлена", Foreground = Brushes.MediumPurple, Margin = new(0, 14, 0, 14) };
    private readonly StackPanel transcriptHost = new();
    private AudioCapture? audioCapture;
    private AudioQueue? audioQueue;
    private CancellationTokenSource? audioCancellation;
    private Task? audioWorker;
    private int audioGeneration;
    private readonly List<TranscriptLine> transcript = [];
    private record TranscriptLine(DateTimeOffset Time, string Source, string Text);
    private void InitializeAudio()
    {
        SettingsHost.Children.Add(new TextBlock { Text = "Распознавание речи", FontSize = 22, Margin = new(0, 28, 0, 16) });
        audioEndpoint = AddTextSetting("Адрес API транскрибации", settings.AudioEndpoint);
        audioModel = AddTextSetting("Модель транскрибации — ID у провайдера", settings.AudioModel);
        SettingsHost.Children.Add(new TextBlock { Text = "API-ключ транскрибации (отдельный от чата)" });
        audioKey = new PasswordBox(); SettingsHost.Children.Add(audioKey);
        try { audioKey.Password = SecretStore.Unprotect(settings.ProtectedAudioKey); } catch { vm.Status = "Введите ключ транскрибации заново."; }
        var save = new Button { Content = "Сохранить настройки речи", HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += (_, _) =>
        {
            try { CompatibleProvider.Endpoint(audioEndpoint.Text, "audio/transcriptions"); settings.AudioEndpoint = audioEndpoint.Text.Trim(); settings.AudioModel = audioModel.Text.Trim(); settings.ProtectedAudioKey = SecretStore.Protect(audioKey.Password.Trim()); settingsStore.Save(settings); vm.Status = "Настройки речи сохранены."; }
            catch { vm.Status = "Не удалось сохранить настройки речи. Проверьте адрес и права доступа."; }
        };
        SettingsHost.Children.Add(save);
        ConversationPage.Children.Clear();
        var dock = new DockPanel(); ConversationPage.Children.Add(dock);
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Слушать. Понимать. Помогать.", FontSize = 25, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text = "Выберите источники. Разговор хранится только в памяти.", Foreground = (Brush)FindResource("Muted"), Margin = new(0, 10, 0, 14) });
        top.Children.Add(new TextBlock { Text = "Микрофон" }); microphones = new ComboBox { DisplayMemberPath = "Name" }; top.Children.Add(microphones);
        top.Children.Add(new TextBlock { Text = "Звук компьютера" }); outputs = new ComboBox { DisplayMemberPath = "Name" }; top.Children.Add(outputs);
        var row = new WrapPanel(); top.Children.Add(row);
        recordButton = new Button { Content = "Начать запись", Background = new SolidColorBrush(Color.FromRgb(104,80,201)) }; recordButton.Click += (_, _) => { if (audioCapture is null) StartAudio(); else StopAudio(); }; row.Children.Add(recordButton);
        var refresh = new Button { Content = "Обновить устройства" }; refresh.Click += (_, _) => { if (audioCapture is null) RefreshDevices(); }; row.Children.Add(refresh);
        top.Children.Add(recordingStatus);
        dock.Children.Add(new ScrollViewer { Content = transcriptHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        RefreshDevices();
        Closing += (_, _) => StopAudio();
        AddConversationActions(top);
    }
    partial void AddConversationActions(StackPanel panel);
    private void RefreshDevices()
    {
        try { microphones.ItemsSource = AudioCapture.Devices(DataFlow.Capture); outputs.ItemsSource = AudioCapture.Devices(DataFlow.Render); microphones.SelectedIndex = outputs.SelectedIndex = 0; }
        catch { vm.Status = "Не удалось получить аудиоустройства. Проверьте службу звука Windows."; }
    }
    private void StartAudio()
    {
        try
        {
            var options = new ProviderOptions(audioEndpoint.Text.Trim(), audioKey.Password.Trim(), audioModel.Text.Trim()); CompatibleProvider.Validate(options);
            var mic = (microphones.SelectedItem as AudioDevice)?.Id ?? ""; var output = (outputs.SelectedItem as AudioDevice)?.Id ?? "";
            if (mic.Length == 0 && output.Length == 0) throw new ProviderException("Выберите хотя бы одно устройство.");
            if (MessageBox.Show(this, $"Аудио выбранных устройств будет передаваться на {new Uri(options.Endpoint).Host} для распознавания. Звук компьютера включает все приложения на выбранном устройстве. Аудиофайлы не сохраняются. Начать?", "Передача аудио провайдеру", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            audioCancellation = new(); audioQueue = new(4); audioCapture = new(); int generation = ++audioGeneration;
            var queue = audioQueue;
            audioCapture.Chunk += chunk => { if (!queue.TryAdd(chunk)) Dispatcher.BeginInvoke(() => { if (generation == audioGeneration) vm.Status = "Провайдер не успевает: фрагмент пропущен, очередь заполнена."; }); };
            audioCapture.DeviceFailed += () => Dispatcher.BeginInvoke(() => { if (generation == audioGeneration) { StopAudio(); vm.Status = "Аудиоустройство отключено или недоступно. Выберите устройство заново."; } });
            audioCapture.Start(mic, output);
            audioWorker = ConsumeAudioAsync(queue, options, audioCancellation.Token, generation);
            microphones.IsEnabled = outputs.IsEnabled = false; recordButton.Content = "Остановить запись"; recordingStatus.Text = "●  Идёт запись · аудио передаётся провайдеру";
        }
        catch (Exception ex) { StopAudio(); vm.Status = ex is ProviderException ? ex.Message : "Не удалось начать запись. Проверьте устройства и разрешение микрофона Windows."; }
    }
    private async Task ConsumeAudioAsync(AudioQueue queue, ProviderOptions options, CancellationToken token, int generation)
    {
        var transcriber = new TranscriptionProvider(http);
        try
        {
            await foreach (var chunk in queue.ReadAsync(token))
            {
                using (chunk)
                {
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(40));
                        string text = await transcriber.TranscribeAsync(options, chunk.Data, timeout.Token);
                        if (generation != audioGeneration || token.IsCancellationRequested || text.Length == 0) continue;
                        var line = new TranscriptLine(chunk.Time, chunk.Source, text.Length > 8000 ? text[..8000] : text); transcript.Add(line);
                        OnTranscriptAdded();
                        transcriptHost.Children.Add(new TextBlock { Text = $"{line.Time.ToLocalTime():HH:mm:ss} · {line.Source}\n{line.Text}", Margin = new(0, 0, 0, 14), LineHeight = 22 });
                        while (transcriptHost.Children.Count > 100) transcriptHost.Children.RemoveAt(0);
                        while (transcript.Count > 200) transcript.RemoveAt(0);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                    catch (Exception ex) { if (generation == audioGeneration) vm.Status = ex is OperationCanceledException ? "Транскрипция не успела обработать фрагмент за 40 секунд." : MainViewModel.FriendlyError(ex); }
                }
            }
        }
        catch (OperationCanceledException) { }
        finally { queue.Dispose(); }
    }
    partial void OnTranscriptAdded();
    private void StopAudio()
    {
        ++audioGeneration; audioCancellation?.Cancel();
        try { audioCapture?.Dispose(); } catch { vm.Status = "Запись остановлена с ошибкой устройства."; }
        audioCapture = null; audioQueue?.Dispose(); audioQueue = null;
        var cancellation = audioCancellation; audioCancellation = null;
        if (audioWorker is { } worker) _ = worker.ContinueWith(_ => cancellation?.Dispose()); else cancellation?.Dispose();
        audioWorker = null;
        if (recordButton is not null) { recordButton.Content = "Начать запись"; microphones.IsEnabled = outputs.IsEnabled = true; recordingStatus.Text = "●  Запись остановлена"; }
    }
    partial void OnPanic() { panicDuringCapture = true; StopAudio(); vm.Cancel(); summaryCancellation?.Cancel(); regionWindow?.Close(); }
}
