using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GhostGPT.Controls;
using GhostGPT.Services;
using GhostGPT.ViewModels;
namespace GhostGPT;
public partial class MainWindow
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(3) };
    private CompatibleProvider provider = null!;
    private TextBox endpointBox = null!, modelBox = null!, visionModelBox = null!, instructionBox = null!;
    private PasswordBox keyBox = null!;
    private CancellationTokenSource? checkCancellation;
    private CancellationTokenSource? modelsCancellation;
    private string? pendingImage;
    private void InitializeChat()
    {
        provider = new(http);
        SettingsHost.Children.Add(new TextBlock { Text = "Подключение ИИ", FontSize = 22, Margin = new(0, 24, 0, 16) });
        var wellflow = new Button { Content = "Wellflow · GPT-5.6 Sol", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0,0,0,12) };
        SettingsHost.Children.Add(wellflow);
        endpointBox = AddTextSetting("Адрес API (до /v1 включительно)", settings.ChatEndpoint);
        modelBox = AddTextSetting("Модель чата — точный ID у провайдера", settings.ChatModel);
        visionModelBox = AddTextSetting("Модель изображений — пусто для модели чата", settings.VisionModel);
        SettingsHost.Children.Add(new TextBlock { Text = "API-ключ · шифруется для вашей учётной записи Windows" });
        keyBox = new PasswordBox(); SettingsHost.Children.Add(keyBox);
        try { keyBox.Password = SecretStore.Unprotect(settings.ProtectedChatKey); }
        catch { vm.Status = "Сохранённый ключ недоступен. Введите API-ключ заново."; }
        var models = new ComboBox { Visibility = Visibility.Collapsed, ToolTip = "Модели провайдера. Выберите текстовую/vision модель для чата." };
        var loadModels = new Button { Content = "Загрузить модели провайдера", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0,0,0,10) };
        SettingsHost.Children.Add(loadModels); SettingsHost.Children.Add(models);
        void ResetModels() { modelsCancellation?.Cancel(); models.ItemsSource = null; models.Visibility = Visibility.Collapsed; }
        endpointBox.TextChanged += (_, _) => ResetModels();
        keyBox.PasswordChanged += (_, _) => ResetModels();
        wellflow.Click += (_, _) =>
        {
            var preset = ConnectionPresets.Wellflow(ChatOptions());
            endpointBox.Text = preset.Endpoint; modelBox.Text = preset.Model; keyBox.Password = preset.Key;
            visionModelBox.Clear(); ResetModels();
            vm.Status = "Выбран Wellflow / gpt-5.6-sol. Введите ключ Wellflow и сохраните подключение.";
        };
        models.SelectionChanged += (_, _) => { if (models.SelectedItem is string model) modelBox.Text = model; };
        loadModels.Click += async (_, _) =>
        {
            loadModels.IsEnabled = false; modelsCancellation = new(TimeSpan.FromSeconds(20));
            try
            {
                var available = await provider.ModelsAsync(ChatOptions(), modelsCancellation.Token);
                modelsCancellation.Token.ThrowIfCancellationRequested();
                models.ItemsSource = available; models.Visibility = Visibility.Visible;
                if (available.Contains(modelBox.Text.Trim())) models.SelectedItem = modelBox.Text.Trim();
                vm.Status = available.Count == 0 ? "Список моделей пуст. Введите ID вручную." : $"Доступно моделей: {available.Count}. Выберите модель чата; поле ID также можно редактировать.";
            }
            catch (OperationCanceledException) { vm.Status = "Загрузка моделей отменена или истёк тайм-аут."; }
            catch (Exception ex) { vm.Status = MainViewModel.FriendlyError(ex); }
            finally { modelsCancellation.Dispose(); modelsCancellation = null; loadModels.IsEnabled = true; }
        };
        instructionBox = AddTextSetting("Инструкция модели / ваш контекст", settings.Instruction); instructionBox.AcceptsReturn = true; instructionBox.MinHeight = 80; instructionBox.MaxHeight = 180;
        var actions = new WrapPanel();
        var save = new Button { Content = "Сохранить подключение", Background = new SolidColorBrush(Color.FromRgb(104,80,201)) };
        save.Click += (_, _) => { try { SaveConnection(); vm.Status = "Подключение сохранено. Ключ защищён Windows DPAPI."; } catch (Exception ex) { vm.Status = MainViewModel.FriendlyError(ex); } };
        var check = new Button { Content = "Проверить ключ" };
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false; checkCancellation = new(TimeSpan.FromSeconds(20));
            try { await provider.CheckAsync(ChatOptions(), checkCancellation.Token); vm.Status = "Ключ принят: /models доступен. Доступ к выбранной модели проверяется при отправке."; }
            catch (OperationCanceledException) { vm.Status = "Проверка отменена или превышено время ожидания."; }
            catch (Exception ex) { vm.Status = MainViewModel.FriendlyError(ex); }
            finally { checkCancellation.Dispose(); checkCancellation = null; check.IsEnabled = true; }
        };
        actions.Children.Add(save); actions.Children.Add(check); SettingsHost.Children.Add(actions);
        SettingsHost.Children.Add(new TextBlock { Text = "Ключ можно заменить или удалить: очистите поле и сохраните. Текст и вложения передаются выбранному провайдеру только при отправке. Проверка ключа обращается к /models.", Margin = new(0, 12, 0, 16), Foreground = (Brush)FindResource("Muted") });
        Composer.Children.Clear();
        Composer.Children.Add(PromptBox); // detached by Clear above
        PromptBox.MaxLength = 16000;
        var row = new WrapPanel();
        SendButton = new Button { Content = "Отправить ↗", Background = new SolidColorBrush(Color.FromRgb(128,97,232)) };
        SendButton.Click += async (_, _) => await SendChatAsync();
        row.Children.Add(SendButton);
        var cancel = new Button { Content = "Остановить" }; cancel.Click += (_, _) => { vm.Cancel(); summaryCancellation?.Cancel(); }; row.Children.Add(cancel);
        var clear = new Button { Content = "Очистить" }; clear.Click += (_, _) => { vm.ClearChat(); if (!vm.Busy) RenderMessages(); }; row.Children.Add(clear);
        Composer.Children.Add(row);
        vm.Messages.CollectionChanged += (_, _) => RenderMessages();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Busy)) { SendButton.IsEnabled = !vm.Busy; clear.IsEnabled = !vm.Busy; RenderMessages(); } };
        Closing += (_, _) => { vm.Cancel(); checkCancellation?.Cancel(); modelsCancellation?.Cancel(); http.Dispose(); };
        RenderMessages();
    }
    private TextBox AddTextSetting(string label, string text)
    {
        SettingsHost.Children.Add(new TextBlock { Text = label }); var box = new TextBox { Text = text, MaxLength = 16000 }; SettingsHost.Children.Add(box); return box;
    }
    private ProviderOptions ChatOptions(bool vision = false) => new(endpointBox.Text.Trim(), keyBox.Password.Trim(), vision && !string.IsNullOrWhiteSpace(visionModelBox.Text) ? visionModelBox.Text.Trim() : modelBox.Text.Trim());
    private void SaveConnection()
    {
        CompatibleProvider.Endpoint(endpointBox.Text, "models");
        settings.ChatEndpoint = endpointBox.Text.Trim(); settings.ChatModel = modelBox.Text.Trim(); settings.VisionModel = visionModelBox.Text.Trim();
        settings.ProtectedChatKey = SecretStore.Protect(keyBox.Password.Trim()); settings.Instruction = instructionBox.Text;
        settingsStore.Save(settings);
    }
    private async Task SendChatAsync()
    {
        if (vm.Busy || conversationBusy) return;
        var prompt = PromptBox.Text.Trim(); if (prompt.Length == 0) prompt = pendingImage is null ? "" : "Проанализируй изображение и помоги с задачей.";
        if (prompt.Length == 0) return;
        try
        {
            var options = ChatOptions(pendingImage is not null); CompatibleProvider.Validate(options);
            var image = pendingImage; pendingImage = null; OnAttachmentChanged(); PromptBox.Clear();
            await vm.SendAsync(provider, options, instructionBox.Text, prompt, image);
        }
        catch (Exception ex) { vm.Status = MainViewModel.FriendlyError(ex); }
    }
    partial void OnAttachmentChanged();
    private void RenderMessages()
    {
        MessagesHost.Children.Clear();
        var messages = compact ? vm.Messages.TakeLast(1) : vm.Messages.AsEnumerable();
        foreach (var message in messages)
        {
            var panel = new StackPanel { Margin = new(0, 0, 0, 20) };
            var head = new DockPanel { Margin = new(0, 0, 0, 8) };
            var copy = new Button { Content = "Копировать", Padding = new(8, 4, 8, 4), FontSize = 11 }; copy.Click += (_, _) => MarkdownView.Copy(message.Text); DockPanel.SetDock(copy, Dock.Right); head.Children.Add(copy);
            head.Children.Add(new TextBlock { Text = message.Role == "user" ? "ВЫ" : "◈  GHOST", Foreground = Brushes.MediumPurple, FontWeight = FontWeights.SemiBold }); panel.Children.Add(head);
            if (vm.Busy && message == vm.Messages.LastOrDefault())
            {
                var text = new TextBlock { LineHeight = 23 }; text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(message.Text)) { Source = message }); panel.Children.Add(text);
            }
            else panel.Children.Add(MarkdownView.Render(message.Text));
            MessagesHost.Children.Add(panel);
        }
        if (vm.Messages.Count == 0) MessagesHost.Children.Add(new TextBlock { Text = "С чего начнём?\n\nВведите API-ключ и модель в настройках, затем задайте вопрос.", Foreground = (Brush)FindResource("Muted"), FontSize = 17 });
    }
}
