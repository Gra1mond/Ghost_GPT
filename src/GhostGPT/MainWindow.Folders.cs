using System.Windows;
using System.Windows.Controls;
using GhostGPT.Services;
namespace GhostGPT;
public partial class MainWindow
{
    private string? selectedFolder;
    private readonly HashSet<string> closedFolders = [];
    private void RefreshFolders() => RefreshHistory();
    private void ToggleHistorySearch(object sender, RoutedEventArgs e)
    {
        SearchPanel.Visibility = SearchPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (SearchPanel.Visibility == Visibility.Visible) HistorySearch.Focus(); else HistorySearch.Clear();
    }
    private void ToggleFolderCreate(object sender, RoutedEventArgs e)
    {
        FolderCreatePanel.Visibility = FolderCreatePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (FolderCreatePanel.Visibility == Visibility.Visible) FolderName.Focus();
    }
    private void CancelFolderCreate(object sender, RoutedEventArgs e)
    {
        FolderName.Clear(); FolderCreatePanel.Visibility = Visibility.Collapsed; NewFolderToggle.Focus();
    }
    private void FolderEditorKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; CancelFolderCreate(sender,e); }
        else if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; CreateFolder(sender,e); }
    }
    private void CreateFolder(object sender, RoutedEventArgs e)
    {
        string name = FolderName.Text.Trim();
        if (name.Length == 0) { FolderName.Focus(); return; }
        settings.ChatFolders ??= [];
        if (!settings.ChatFolders.Contains(name)) settings.ChatFolders.Add(name);
        closedFolders.Remove(name); FolderName.Clear(); FolderCreatePanel.Visibility = Visibility.Collapsed; SaveSettings(); RefreshHistory();
    }
    private void MoveChat(SavedChat chat, string? folder)
    {
        var previous = chat.Folder; chat.Folder = folder;
        try { new ChatArchive(settingsStore.DirectoryPath).Save(savedChats); }
        catch { chat.Folder = previous; vm.Status = "Не удалось переместить чат."; return; }
        RefreshHistory();
    }
    private void GoToLatest(object sender, RoutedEventArgs e) => MessageScroll.JumpToLatest();
}
