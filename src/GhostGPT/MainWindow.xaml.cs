using System.Windows;
using System.Windows.Input;
using GhostGPT.ViewModels;
namespace GhostGPT;
public partial class MainWindow : Window
{
    private readonly MainViewModel vm = new();
    private bool compact;
    private double expandedHeight = 790;
    public MainWindow() { InitializeComponent(); DataContext = vm; InitializeWindowFeatures(); InitializeChat(); InitializeScreenshots(); InitializeAudio(); }
    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is System.Windows.Controls.TextBlock or System.Windows.Controls.Grid) DragMove(); }
    private void ShowChat(object s, RoutedEventArgs e) => Navigate(ChatPage);
    private void ShowConversation(object s, RoutedEventArgs e) => Navigate(ConversationPage);
    private void ShowSettings(object s, RoutedEventArgs e) => Navigate(SettingsPage);
    private void Navigate(UIElement page) { ChatPage.Visibility = ConversationPage.Visibility = SettingsPage.Visibility = Visibility.Collapsed; page.Visibility = Visibility.Visible; }
    private void MinimizeWindow(object s, RoutedEventArgs e) { if (windowService?.CanHide == true) Hide(); else WindowState = WindowState.Minimized; }
    private void CloseWindow(object s, RoutedEventArgs e) => Close();
    private void ToggleCompact(object s, RoutedEventArgs e)
    {
        compact = !compact;
        if (compact) { expandedHeight = Height; Height = 430; Navigate(ChatPage); } else Height = expandedHeight;
        Navigation.Visibility = Welcome.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        RenderMessages();
    }
}
