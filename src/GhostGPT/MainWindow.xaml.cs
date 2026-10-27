using System.Windows;
using System.Windows.Input;
using GhostGPT.ViewModels;
namespace GhostGPT;
public partial class MainWindow : Window
{
    private readonly MainViewModel vm = new();
    private bool compact;
    private double expandedHeight = 790;
    public MainWindow() : this(new Services.SettingsStore()) { }
    public MainWindow(Services.SettingsStore store)
    {
        settingsStore = store; InitializeComponent(); DataContext = vm; InitializeWindowFeatures();
        int appearanceCount = SettingsHost.Children.Count;
        InitializeChat(); InitializeScreenshots(); InitializeAudio();
        for (int i = 0; i < appearanceCount; i++) { var control = SettingsHost.Children[0]; SettingsHost.Children.RemoveAt(0); SettingsHost.Children.Add(control); }
    }
    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is System.Windows.Controls.TextBlock or System.Windows.Controls.Grid) DragMove(); }
    private void ShowChat(object s, RoutedEventArgs e) => Navigate(ChatPage);
    private void ShowConversation(object s, RoutedEventArgs e) => Navigate(ConversationPage);
    private void ShowSettings(object s, RoutedEventArgs e) => Navigate(SettingsPage);
    private void Navigate(UIElement page)
    {
        ChatPage.Visibility = ConversationPage.Visibility = SettingsPage.Visibility = Visibility.Collapsed; page.Visibility = Visibility.Visible;
        for (int i = 0; i < Navigation.Children.Count; i++)
            ((System.Windows.Controls.Button)Navigation.Children[i]).Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48,49,62));
        int selected = page == ChatPage ? 0 : page == ConversationPage ? 1 : 2;
        ((System.Windows.Controls.Button)Navigation.Children[selected]).Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(104,80,201));
    }
    private void MinimizeWindow(object s, RoutedEventArgs e) { if (windowService?.CanHide == true) Hide(); else WindowState = WindowState.Minimized; }
    private void CloseWindow(object s, RoutedEventArgs e) => Close();
    private void ToggleCompact(object s, RoutedEventArgs e)
    {
        compact = !compact;
        if (compact) { expandedHeight = Height; Height = 430; Navigate(ChatPage); } else Height = expandedHeight;
        Navigation.Visibility = Welcome.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Composer.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactActions.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        RenderMessages();
    }
    private void CompactAnswer(object s, RoutedEventArgs e) => OnAnswerRequested();
    private void CompactCancel(object s, RoutedEventArgs e) { vm.Cancel(); summaryCancellation?.Cancel(); }
    private void CompactCopy(object s, RoutedEventArgs e) { if (vm.Messages.LastOrDefault() is { } last) Controls.MarkdownView.Copy(last.Text); }
}
