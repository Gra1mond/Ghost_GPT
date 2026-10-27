using System.Windows;
using System.Windows.Controls;
using GhostGPT.Services;
namespace GhostGPT;
public partial class MainWindow
{
    private readonly SettingsStore settingsStore;
    private Settings settings = new();
    private WindowService? windowService;
    private TextBlock captureStatus = new();
    private void InitializeWindowFeatures()
    {
        settings = settingsStore.Load();
        sidebarWidth = double.IsFinite(settings.SidebarWidth) ? Math.Clamp(settings.SidebarWidth, 200, 480) : 280;
        vm.BackgroundOpacity = settings.Opacity;
        SourceInitialized += (_, _) =>
        {
            windowService = new(this);
            windowService.RestoreBounds(settings);
            captureStatus.Text = windowService.Apply(settings);
            var registration = windowService.Register(settings);
            vm.Status = settingsStore.LoadWarning is { } warning ? warning + " " + registration : registration;
            windowService.Hotkey += HandleHotkey;
        };
        Closing += (_, e) => { if (e.Cancel) return; SaveSettings(); windowService?.Dispose(); };
        SettingsHost.Children.Add(captureStatus);
        var top = new CheckBox { Content = "Поверх приложений", IsChecked = settings.AlwaysOnTop };
        top.Click += (_, _) => { settings.AlwaysOnTop = top.IsChecked == true; ApplyWindowSettings(); };
        SettingsHost.Children.Add(top);
        var exclude = new CheckBox { Content = "Исключать окно из захвата", IsChecked = settings.ExcludeCapture };
        exclude.Click += (_, _) => { settings.ExcludeCapture = exclude.IsChecked == true; ApplyWindowSettings(); };
        SettingsHost.Children.Add(exclude);
        SettingsHost.Children.Add(new TextBlock { Text = "Горячие клавиши: Ctrl + Shift + выбранная клавиша", Margin = new(0, 20, 0, 8) });
        AddKeySetting("Вернуть поверх / отправить назад", settings.ToggleKey, v => settings.ToggleKey = v);
        AddKeySetting("Ответ по разговору", settings.AnswerKey, v => settings.AnswerKey = v);
        AddKeySetting("Скриншот", settings.ScreenshotKey, v => settings.ScreenshotKey = v);
        AddKeySetting("Скрыть и остановить запись", settings.PanicKey, v => settings.PanicKey = v);
        AddKeySetting("Отправить за другие окна", settings.SendBehindKey, v => { settings.SendBehindKey = v; UpdateBehindTooltip(); });
        SettingsHost.Children.Add(new TextBlock { Text = "Обычное скрытие оставляет сессию активной. Для остановки используйте Ctrl+Shift+H. Исключение из захвата не гарантирует защиту во всех программах.", Margin = new(0, 10, 0, 20) });
    }
    private void AddKeySetting(string label, string value, Action<string> set)
    {
        SettingsHost.Children.Add(new TextBlock { Text = label });
        var box = new TextBox { Text = value };
        box.LostKeyboardFocus += (_, _) => { set(box.Text.Trim()); vm.Status = windowService?.Register(settings) ?? ""; SaveSettings(); };
        SettingsHost.Children.Add(box);
    }
    private void ApplyWindowSettings() { captureStatus.Text = windowService?.Apply(settings) ?? ""; SaveSettings(); }
    private void SaveSettings()
    {
        settings.SidebarWidth = sidebarWidth;
        var bounds = fullscreen ? beforeFullscreen : WindowState == WindowState.Normal ? new Rect(Left,Top,Width,compact ? expandedHeight : Height) : RestoreBounds;
        settings.Left = bounds.Left; settings.Top = bounds.Top; settings.Width = bounds.Width; settings.Height = bounds.Height; settings.Opacity = vm.BackgroundOpacity;
        try { settingsStore.Save(settings); } catch { vm.Status = "Не удалось сохранить настройки."; }
    }
    private void HandleHotkey(int id)
    {
        if (id == 1) { if (IsVisible && IsActive && !sentBehind && WindowState != WindowState.Minimized) SendBehind(this, new()); else BringForward(); }
        if (id == 2) OnAnswerRequested();
        if (id == 3) OnScreenshotRequested();
        if (id == 4) { OnPanic(); if (trayIcon is not null || windowService?.CanHide == true) Hide(); else WindowState = WindowState.Minimized; }
        if (id == 5) SendBehind(this, new());
    }
    partial void OnAnswerRequested();
    partial void OnScreenshotRequested();
    partial void OnPanic();
}
