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
    private WrapPanel composerTools = null!;
    private Button stopButton = null!;
    private void UpdateComposerActions()
    {
        bool generating = vm.Busy || conversationBusy;
        SendButton.Visibility = generating ? Visibility.Collapsed : Visibility.Visible;
        SendButton.IsEnabled = !generating;
        stopButton.Visibility = generating ? Visibility.Visible : Visibility.Collapsed;
    }
    private void InitializeChat()
    {
        provider = new(http);
        SettingsHost.Children.Add(new TextBlock { Text = "Подключение ИИ", FontSize = 22, Margin = new(0, 24, 0, 16) });
        var wellflow = new Button { Content = "Wellflow · GPT-5.6 Sol", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0,0,0,12) };
        SettingsHost.Children.Add(wellflow);
        endpointBox = AddTextSetting("Адрес API (до /v1 включительно)", settings.ChatEndpoint);
        modelBox = AddTextSetting("Модель чата — точный ID у провайдера", settings.ChatModel);
        ModelLabel.Text = string.IsNullOrWhiteSpace(modelBox.Text) ? "Выберите модель в настройках" : modelBox.Text;
        modelBox.TextChanged += (_, _) => ModelLabel.Text = string.IsNullOrWhiteSpace(modelBox.Text) ? "Выберите модель в настройках" : modelBox.Text;
        visionModelBox = AddTextSetting("Модель изображений — пусто для модели чата", settings.VisionModel);
        SettingsHost.Children.Add(new TextBlock { Text = "API-ключ · шифруется для вашей учётной записи Windows" });
        keyBox = new PasswordBox(); SettingsHost.Children.Add(keyBox);
        try { keyBox.Password = SecretStore.Unprotect(settings.ProtectedChatKey); }
        catch { vm.Status = "Сохранённый ключ недоступен. Введите API-ключ заново."; }
        var original = ChatOptions();
        var corrected = ConnectionPresets.CorrectKnownMismatch(original);
        if (corrected.Endpoint != original.Endpoint)
        {
            endpointBox.Text = corrected.Endpoint; settings.ChatEndpoint = corrected.Endpoint;
            SettingsHost.Children.Add(new TextBlock { Text = "Обнаружен ключ Wellflow: адрес старой версии исправлен на api.wellflow.dev/v1.", Margin = new(0,0,0,12) });
            try { settingsStore.Save(settings); } catch { vm.Status = "Адрес исправлен для этой сессии. Сохраните подключение вручную."; }
        }
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
        instructionBox = AddTextSetting("Дополнения к INTERVIEW.md / вакансия, стек, опыт", settings.Instruction); instructionBox.AcceptsReturn = true; instructionBox.MinHeight = 80; instructionBox.MaxHeight = 180;
        var actions = new WrapPanel();
        var save = new Button { Content = "Сохранить подключение", Background = new SolidColorBrush(Color.FromRgb(72,72,72)) };
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
        var advanced = new StackPanel { Margin = new(0,12,0,0) };
        foreach (var field in new[] { visionModelBox, instructionBox })
        {
            int index = SettingsHost.Children.IndexOf(field);
            var label = SettingsHost.Children[index - 1];
            SettingsHost.Children.Remove(label); SettingsHost.Children.Remove(field);
            advanced.Children.Add(label); advanced.Children.Add(field);
        }
        SettingsHost.Children.Add(new Expander { Header = "Дополнительно: изображения и инструкция", Foreground = Brushes.LightGray, Content = advanced, Margin = new(0,4,0,12) });
        Composer.Children.Clear();
        var editor = new Grid(); editor.Children.Add(PromptBox);
        var hint = new TextBlock { Text = "Напишите сообщение…", Foreground = Brushes.DarkGray, Margin = new(2,6,0,0), IsHitTestVisible = false, FontSize = 15 };
        editor.Children.Add(hint); Composer.Children.Add(editor);
        PromptBox.TextChanged += (_, _) => hint.Visibility = PromptBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        PromptBox.PreviewKeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && !System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) { e.Handled = true; await SendChatAsync(); } };
        PromptBox.MaxLength = 16000;
        var row = new DockPanel { Margin = new(0,8,0,0) };
        SendButton = new Button { Content = new TextBlock { Text = "↑", Foreground = Brushes.Black, FontSize = 21 }, ToolTip = "Отправить · Enter", Background = Brushes.White, Padding = new(12,4,12,4), Margin = new(0) };
        SendButton.Click += async (_, _) => await SendChatAsync();
        var actionSlot = new Grid { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(8,0,0,0) };
        DockPanel.SetDock(actionSlot,Dock.Right); row.Children.Add(actionSlot);
        SendButton.Width = SendButton.Height = 40; SendButton.Padding = new(10);
        actionSlot.Children.Add(SendButton);
        stopButton = new Button { Content = Controls.Icons.Make("stop"), ToolTip = "Остановить ответ", Background = Brushes.Transparent, Width = 40, Height = 40, Padding = new(10), Margin = new(0), Visibility = Visibility.Collapsed };
        stopButton.Click += (_, _) => { vm.Cancel(); summaryCancellation?.Cancel(); };
        actionSlot.Children.Add(stopButton);
        composerTools = new WrapPanel(); row.Children.Add(composerTools);
        Composer.Children.Add(row);
        vm.Messages.CollectionChanged += (_, _) => RenderMessages();
        MessageScroll.ScrollChanged += (_, _) => LatestButton.Visibility = MessageScroll.ScrollableHeight - MessageScroll.VerticalOffset > 80 ? Visibility.Visible : Visibility.Collapsed;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Busy)) { UpdateComposerActions(); RenderMessages(); } };
        Closing += (_, e) => { if (e.Cancel) return; vm.Cancel(); checkCancellation?.Cancel(); modelsCancellation?.Cancel(); http.Dispose(); };
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
            if (!SaveInstructions()) return;
            var instructions = InterviewInstructions.Load(instructionBox.Text);
            var image = pendingImage; pendingImage = null; OnAttachmentChanged(); PromptBox.Clear();
            await vm.SendAsync(provider, options, instructions, prompt, image);
        }
        catch (Exception ex) { vm.Status = MainViewModel.FriendlyError(ex); }
    }
    partial void OnAttachmentChanged();
    private readonly Dictionary<ChatMessage, (string Text, FrameworkElement View)> messageViews = [];
    private void RenderMessages()
    {
        MessagesHost.Children.Clear();
        foreach (var stale in messageViews.Keys.Where(m => !vm.Messages.Contains(m)).ToArray()) messageViews.Remove(stale);
        var messages = compact ? vm.Messages.TakeLast(1) : vm.Messages.AsEnumerable();
        foreach (var message in messages)
        {
            bool streaming = vm.Busy && message == vm.Messages.LastOrDefault();
            if (!streaming && messageViews.TryGetValue(message, out var cached) && cached.Text == message.Text) { MessagesHost.Children.Add(cached.View); continue; }
            var panel = new StackPanel();
            var head = new DockPanel { Margin = new(0, 0, 0, 16) };
            var copy = new Button { Content = "Копировать", Background = Brushes.Transparent, Padding = new(8, 4, 8, 4), FontSize = 11 }; copy.Click += (_, _) => MarkdownView.Copy(message.Text); DockPanel.SetDock(copy, Dock.Right); head.Children.Add(copy);
            var identity = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            identity.Children.Add(new Border { Child = new TextBlock { Text = message.Role == "user" ? "В" : "G", FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, Width = 28, Height = 28, CornerRadius = new(9), Background = new SolidColorBrush(message.Role == "user" ? Color.FromArgb(90,100,110,130) : Color.FromArgb(110,130,95,200)), Margin = new(0,0,10,0) });
            if (message.Role != "user")
            {
                identity.Children.Clear();
                var ghost = GhostVisual.Create(32);
                ghost.Loaded += (_, _) => GhostVisual.Animate(ghost, streaming && vm.Busy);
                ghost.Unloaded += (_, _) => GhostVisual.Animate(ghost, false);
                identity.Children.Add(ghost);
            }
            identity.Children.Add(new TextBlock { Text = message.Role == "user" ? "Вы" : streaming ? "Ghost · отвечает…" : "Ghost", Foreground = Brushes.LightGray, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(identity); panel.Children.Add(head);
            if (vm.Busy && message == vm.Messages.LastOrDefault())
            {
                var text = new TextBox { IsReadOnly = true, Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(0), Margin = new(0), TextWrapping = TextWrapping.Wrap, SelectionBrush = Brushes.MediumPurple }; text.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(message.Text)) { Source = message, Mode = System.Windows.Data.BindingMode.OneWay }); panel.Children.Add(text);
            }
            else panel.Children.Add(MarkdownView.Render(message.Text));
            bool user = message.Role == "user";
            MessagesHost.Children.Add(new Border { Child = panel, Background = new SolidColorBrush(user ? Color.FromArgb(48,112,119,140) : Color.FromArgb(34,117,97,155)), BorderBrush = new SolidColorBrush(user ? Color.FromArgb(35,180,185,200) : Color.FromArgb(65,160,135,210)), BorderThickness = new(1), CornerRadius = new(18), Padding = new(18), Margin = new(user ? 32 : 0,0,user ? 0 : 12,20), HorizontalAlignment = HorizontalAlignment.Stretch });
            if (!streaming) messageViews[message] = (message.Text, (FrameworkElement)MessagesHost.Children[MessagesHost.Children.Count - 1]);
        }
        Welcome.Visibility = vm.Messages.Count == 0 && !compact ? Visibility.Visible : Visibility.Collapsed;
    }
}
