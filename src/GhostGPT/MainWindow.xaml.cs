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
        settingsStore = store; InitializeComponent(); DataContext = vm; InitializeWindowFeatures();
        int appearanceCount = SettingsHost.Children.Count;
        InitializeChat(); int connectionEnd = SettingsHost.Children.Count;
        InitializeScreenshots(); InitializeAudio();
        OrganizeSettings(appearanceCount, connectionEnd);
        Navigate(ChatPage); UpdateSidebar();
    }
    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node)) if (node is ButtonBase) return;
        DragMove();
    }
    private void ResizeWindow(object sender, DragDeltaEventArgs e) { Width = Math.Max(MinWidth, Width + e.HorizontalChange); Height = Math.Max(MinHeight, Height + e.VerticalChange); }
    private void WindowSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSidebar();
    private void UpdateSidebar()
    {
        if (Sidebar is null || Navigation is null) return;
        bool narrow = (WindowSurface.ActualWidth > 0 ? WindowSurface.ActualWidth : Width) < 900;
        SidebarColumn.Width = new GridLength(compact ? 0 : narrow ? 64 : 180);
        Sidebar.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Sidebar.Padding = narrow ? new Thickness(6,20,6,16) : new Thickness(12,20,12,16);
        BrandLabel.Text = narrow ? "◈" : "◈  Ghost"; BrandLabel.Margin = new(8,2,0,24);
        NewChatButton.Content = narrow ? "＋" : "＋  Новый чат";
        SidebarNote.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        var labels = narrow ? new[] { "○", "≋", "⚙" } : new[] { "○  Чат", "≋  Разговор", "⚙  Настройки" };
        for (int i=0;i<3;i++) ((Button)Navigation.Children[i]).Content = labels[i];
    }
    private void NewChat(object sender, RoutedEventArgs e)
    {
        if (vm.Busy || conversationBusy) { vm.Status = "Сначала остановите текущий ответ."; return; }
        vm.ClearChat(); pendingImage = null; OnAttachmentChanged(); PromptBox.Clear(); Navigate(ChatPage); RenderMessages(); PromptBox.Focus();
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
        ChatPage.Visibility = ConversationPage.Visibility = SettingsPage.Visibility = Visibility.Collapsed; page.Visibility = Visibility.Visible;
        for (int i = 0; i < Navigation.Children.Count; i++)
            ((Button)Navigation.Children[i]).Background = Brushes.Transparent;
        int selected = page == ChatPage ? 0 : page == ConversationPage ? 1 : 2;
        ((Button)Navigation.Children[selected]).Background = new SolidColorBrush(Color.FromArgb(100,90,90,90));
        PageTitle.Text = selected == 0 ? "Чат" : selected == 1 ? "Разговор" : "Настройки";
    }
    private void MinimizeWindow(object s, RoutedEventArgs e) { if (windowService?.CanHide == true) Hide(); else WindowState = WindowState.Minimized; }
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
