using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GhostGPT.ViewModels;
namespace GhostGPT;
public partial class MainWindow : Window
{
    private readonly MainViewModel vm = new();
    private bool compact;
    private double expandedHeight = 820;
    public MainWindow() : this(new Services.SettingsStore()) { }
    public MainWindow(Services.SettingsStore store)
    {
        settingsStore = store; InitializeComponent(); DataContext = vm; InitializeInstructionEditor(); InitializeWindowFeatures();
        int appearanceCount = SettingsHost.Children.Count;
        InitializeChat(); int connectionEnd = SettingsHost.Children.Count;
        InitializeScreenshots(); InitializeAudio();
        OrganizeSettings(appearanceCount, connectionEnd);
        Navigate(ChatPage); UpdateSidebar(); InitializeHistory(); InitializeWorkspaceControls(); InitializeTray();
    }
    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (e.GetPosition(WindowSurface).Y > 82) return;
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = node is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(node)) if (node is ButtonBase or TextBoxBase or Thumb or Slider) return;
        if (fullscreen) return;
        e.Handled = true;
        if (e.ClickCount == 2) { ToggleMaximize(sender, e); return; }
        if (WindowState == WindowState.Maximized)
        {
            var local = e.GetPosition(this);
            double ratio = Math.Clamp(local.X / ActualWidth, 0, 1);
            var screen = PointToScreen(local);
            var device = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var cursor = device.Transform(screen);
            WindowState = WindowState.Normal;
            Left = cursor.X - Width * ratio; Top = cursor.Y - Math.Min(local.Y, 40);
        }
        DragMove();
    }
    private void ResizeWindow(object sender, DragDeltaEventArgs e) { Width = Math.Max(MinWidth, Width + e.HorizontalChange); Height = Math.Max(MinHeight, Height + e.VerticalChange); }
    private void WindowSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSidebar();
    private bool? sidebarOpen;
    private double sidebarWidth = 280;
    private void ResizeSidebar(object sender, DragDeltaEventArgs e)
    {
        double available = Math.Max(200, Math.Min(480, ActualWidth - 480));
        sidebarWidth = Math.Clamp(sidebarWidth + e.HorizontalChange, 200, available);
        UpdateSidebar();
    }
    private void FinishSidebarResize(object sender, DragCompletedEventArgs e) => SaveSettings();
    private void ToggleSidebar(object sender, RoutedEventArgs e) { sidebarOpen = Sidebar.Visibility != Visibility.Visible; UpdateSidebar(); }
    private void UpdateSidebar()
    {
        if (Sidebar is null || Navigation is null) return;

        bool show = !compact && (sidebarOpen ?? true);
        SidebarColumn.Width = new GridLength(show ? sidebarWidth : 0);
        MinWidth = show ? sidebarWidth + 480 : 480;
        if (Width < MinWidth) Width = MinWidth;
        Grid.SetColumnSpan(Sidebar, 1);
        Sidebar.Width = sidebarWidth; Sidebar.HorizontalAlignment = HorizontalAlignment.Left;
        Sidebar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SidebarResizer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SidebarToggle.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        NewChatButton.Content = Controls.Icons.Make("compose", "Новый чат");
        SearchToggle.Content = Controls.Icons.Make("search"); SidebarToggle.Content = Controls.Icons.Make("sidebar"); SidebarCollapse.Content = Controls.Icons.Make("sidebar");
        for (int i=0;i<4;i++) ((Button)Navigation.Children[i]).Content = Controls.Icons.Make(new[] { "chat", "audio", "settings", "compose" }[i], new[] { "Текущий чат", "Разговор", "Настройки", "Инструкции" }[i]);
    }
    private void NewChat(object sender, RoutedEventArgs e)
    {
        if (vm.Busy || conversationBusy) { vm.Status = "Сначала остановите текущий ответ."; return; }
        PersistChat(); selectedFolder = null; activeChat = Guid.NewGuid(); vm.ClearChat(); RefreshHistory(); pendingImage = null; OnAttachmentChanged(); PromptBox.Clear(); Navigate(ChatPage); RenderMessages(); PromptBox.Focus();
    }
    private void OrganizeSettings(int appearanceCount, int connectionEnd)
    {
        var children = SettingsHost.Children.Cast<UIElement>().ToArray(); SettingsHost.Children.Clear();
        var groups = new[] { new StackPanel(), new StackPanel(), new StackPanel() };
        for (int i=0;i<children.Length;i++) groups[i < appearanceCount ? 2 : i < connectionEnd ? 0 : 1].Children.Add(children[i]);
        var tabs = new WrapPanel { Margin = new(0,6,0,20) }; SettingsHost.Children.Add(tabs);
        var labels = new[] { "Подключение", "Речь", "Окно" };
        for (int i=0;i<3;i++)
        {
            int selected = i;
            var button = new Button { Content = labels[i], Margin = new(0,0,6,6), Background = i == 0 ? Brushes.DimGray : Brushes.Transparent };
            button.Click += (_, _) =>
            {
                for (int j=0;j<3;j++) { groups[j].Visibility = selected == j ? Visibility.Visible : Visibility.Collapsed; ((Button)tabs.Children[j]).Background = selected == j ? Brushes.DimGray : Brushes.Transparent; }
            };
            tabs.Children.Add(button); groups[i].Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        foreach (var group in groups) SettingsHost.Children.Add(group);
    }
    private void ShowChat(object s, RoutedEventArgs e) => Navigate(ChatPage);
    private void ShowConversation(object s, RoutedEventArgs e) => Navigate(ConversationPage);
    private void ShowSettings(object s, RoutedEventArgs e) => Navigate(SettingsPage);
    private void Navigate(UIElement page)
    {
        ChatPage.Visibility = ConversationPage.Visibility = SettingsPage.Visibility = InstructionsPage.Visibility = Visibility.Collapsed; page.Visibility = Visibility.Visible;
        for (int i = 0; i < Navigation.Children.Count; i++)
            ((Button)Navigation.Children[i]).Background = Brushes.Transparent;
        int selected = page == ChatPage ? 0 : page == ConversationPage ? 1 : page == SettingsPage ? 2 : 3;
        ((Button)Navigation.Children[selected]).Background = new SolidColorBrush(Color.FromArgb(65,100,103,108));

        PageTitle.Text = selected == 0 ? "Чат" : selected == 1 ? "Разговор" : selected == 2 ? "Настройки" : "Инструкции";
    }
    private void MinimizeWindow(object s, RoutedEventArgs e) { if (trayIcon is not null || windowService?.CanHide == true) Hide(); else WindowState = WindowState.Minimized; }
    private void CloseWindow(object s, RoutedEventArgs e) => Close();
    private void ToggleCompact(object s, RoutedEventArgs e)
    {
        compact = !compact;
        if (compact) { expandedHeight = Height; Height = 430; Navigate(ChatPage); } else Height = expandedHeight;
        UpdateSidebar();
        ComposerCard.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactActions.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        RenderMessages();
    }
    private void CompactAnswer(object s, RoutedEventArgs e) => OnAnswerRequested();
    private void CompactCancel(object s, RoutedEventArgs e) { vm.Cancel(); summaryCancellation?.Cancel(); }
    private void CompactCopy(object s, RoutedEventArgs e) { if (vm.Messages.LastOrDefault() is { } last) Controls.MarkdownView.Copy(last.Text); }
}
