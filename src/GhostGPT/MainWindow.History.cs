using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GhostGPT.Services;
namespace GhostGPT;
public partial class MainWindow
{
    private List<SavedChat> savedChats = [];
    private Guid activeChat = Guid.NewGuid();
    private bool restoringChat, archiveAvailable = true;
    private void FilterHistory(object sender, TextChangedEventArgs e) { if (HistoryHost is not null) RefreshHistory(); }
    private void OpenSavedChat(SavedChat chat)
    {
        if (vm.Busy || conversationBusy) { vm.Status = "Остановите ответ перед переключением чата."; return; }
        PersistChat(); activeChat = chat.Id; restoringChat = true;
        try { vm.RestoreChat(chat.Turns); } finally { restoringChat = false; }
        pendingImage = null; OnAttachmentChanged(); PromptBox.Clear(); Navigate(ChatPage); RenderMessages(); RefreshHistory();
    }
    private void DeleteSavedChat(SavedChat chat)
    {
        if (vm.Busy || conversationBusy) { vm.Status = "Остановите ответ перед удалением чата."; return; }
        var remaining = savedChats.Where(c => c.Id != chat.Id).ToList();
        try { new ChatArchive(settingsStore.DirectoryPath).Save(remaining); }
        catch { vm.Status = "Не удалось удалить чат."; return; }
        savedChats = remaining;
        if (chat.Id == activeChat) { vm.ClearChat(); activeChat = Guid.NewGuid(); pendingImage = null; OnAttachmentChanged(); PromptBox.Clear(); }
        RefreshHistory();
    }
    private void InitializeHistory()
    {
        try { savedChats = new ChatArchive(settingsStore.DirectoryPath).Load(); }
        catch { archiveAvailable = false; vm.Status = "История не прочитана. Файл сохранён без изменений."; }
        RefreshFolders();
        RefreshHistory();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Busy) && !vm.Busy) PersistChat(); };
        Closing += (_, e) => { if (!e.Cancel) PersistChat(); };
    }
    private void PersistChat()
    {
        if (restoringChat || !archiveAvailable || vm.Messages.Count == 0) return;
        var chat = savedChats.FirstOrDefault(c => c.Id == activeChat);
        if (chat is null) { chat = new SavedChat { Id = activeChat, Folder = selectedFolder }; savedChats.Add(chat); }
        chat.Turns = vm.Messages.Select(m => new ChatTurn(m.Role,m.Text)).ToList();
        chat.Title = chat.Turns.First().Text.Replace('\n',' ').Trim();
        if (chat.Title.Length > 48) chat.Title = chat.Title[..48] + "…";
        chat.Updated = DateTime.Now;
        try { new ChatArchive(settingsStore.DirectoryPath).Save(savedChats); }
        catch { vm.Status = "Не удалось сохранить историю чатов."; }
        RefreshHistory();
    }
    private void RefreshHistory()
    {

        HistoryHost.Children.Clear();
        string query = HistorySearch.Text.Trim();
        var found = savedChats.Where(c => c.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || c.Turns.Any(t => t.Text.Contains(query, StringComparison.OrdinalIgnoreCase))).OrderByDescending(c => c.Updated).ToList();
        HistoryCount.Text = found.Count == 0 ? (query.Length == 0 ? "Начните новый разговор" : "Ничего не найдено") : "";
        HistoryCount.Visibility = found.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FolderTreeHost.Children.Clear();
        foreach (var chat in found.Where(c => string.IsNullOrEmpty(c.Folder))) HistoryHost.Children.Add(ChatRow(chat));
        var folders = (settings.ChatFolders ?? []).Concat(savedChats.Where(c => !string.IsNullOrEmpty(c.Folder)).Select(c => c.Folder!)).Distinct().OrderBy(n => n);
        foreach (var folder in folders)
        {
            var chats = found.Where(c => c.Folder == folder).ToList();
            if (query.Length > 0 && chats.Count == 0) continue;
            var contents = new StackPanel { Margin = new(18,2,0,8) };
            foreach (var chat in chats) contents.Children.Add(ChatRow(chat));
            var add = new Button { Content = "+ Новый чат", HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, Foreground = Brushes.DarkGray, Padding = new(12,8,12,8), FontSize = 12 };
            add.Click += (_, _) => { if (vm.Busy || conversationBusy) return; NewChat(this,new()); selectedFolder = folder; };
            contents.Children.Add(add);
            var folderHeader = new DockPanel();
            var icon = Controls.Icons.Make("folder"); icon.Margin = new(0,0,10,0); DockPanel.SetDock(icon,Dock.Left); folderHeader.Children.Add(icon);
            folderHeader.Children.Add(new TextBlock { Text = folder, FontSize = 15, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var expander = new Expander { Style = (Style)FindResource("FolderExpander"), Header = folderHeader, ToolTip = folder, Content = contents, IsExpanded = query.Length > 0 || !closedFolders.Contains(folder), Foreground = Brushes.Gainsboro, Margin = new(4,0,4,4), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            expander.Expanded += (_, _) => closedFolders.Remove(folder);
            expander.Collapsed += (_, _) => closedFolders.Add(folder);
            FolderTreeHost.Children.Add(expander);
        }
    }
    private FrameworkElement ChatRow(SavedChat chat)
    {
            var button = new Button { Content = new TextBlock { Text = chat.Title, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 15, FontFamily = new("Segoe UI Variable Text, Segoe UI") }, ToolTip = chat.Title + "\n" + chat.Updated.ToString("g"), Padding = new(12,9,12,9), Margin = new(0,0,0,4), HorizontalContentAlignment = HorizontalAlignment.Left, Background = chat.Id == activeChat ? new SolidColorBrush(Color.FromArgb(100,75,77,81)) : Brushes.Transparent };
            button.Click += (_, _) => OpenSavedChat(chat);
            var menu = new ContextMenu(); var delete = new MenuItem { Header = "Удалить чат" };
            delete.Click += (_, _) => DeleteSavedChat(chat);
            var move = new MenuItem { Header = "Переместить в папку" };
            var unfiled = new MenuItem { Header = "Без папки" }; unfiled.Click += (_, _) => MoveChat(chat, null); move.Items.Add(unfiled);
            foreach (var folder in (settings.ChatFolders ?? []).Concat(savedChats.Where(c => c.Folder is not null).Select(c => c.Folder!)).Distinct().OrderBy(n => n))
            {
                var item = new MenuItem { Header = folder }; item.Click += (_, _) => MoveChat(chat, folder); move.Items.Add(item);
            }
            menu.Items.Add(move); menu.Items.Add(delete); button.ContextMenu = menu;
            var row = new DockPanel();
            var more = new Button { Content = "⋯", Width = 30, Padding = new(4), Margin = new(0,0,0,4), ToolTip = "Действия с чатом", Background = Brushes.Transparent, ContextMenu = menu };
            more.Opacity = 0;
            row.MouseEnter += (_, _) => more.Opacity = 1;
            row.MouseLeave += (_, _) => { if (!row.IsKeyboardFocusWithin) more.Opacity = 0; };
            row.GotKeyboardFocus += (_, _) => more.Opacity = 1;
            row.LostKeyboardFocus += (_, _) => { if (!row.IsMouseOver) more.Opacity = 0; };
            more.Click += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; };
            DockPanel.SetDock(more, Dock.Right); row.Children.Add(more); row.Children.Add(button); return row;
    }
}
