using System.Windows;
using System.Windows.Controls;
using GhostGPT.Services;
namespace GhostGPT;
public partial class MainWindow
{
    private readonly SettingsStore settingsStore = new();
    private Settings settings = new();
    private WindowService? windowService;
    private TextBlock captureStatus = new();
    private void InitializeWindowFeatures()
    {
        settings = settingsStore.Load();
        vm.WindowOpacity = Math.Clamp(settings.Opacity, .55, 1);
        SourceInitialized += (_, _) =>
        {
            windowService = new(this);
            windowService.RestoreBounds(settings);
            captureStatus.Text = windowService.Apply(settings);
            vm.Status = settingsStore.LoadWarning ?? windowService.Register(settings);
            windowService.Hotkey += HandleHotkey;
        };
        Closing += (_, _) => { SaveSettings(); windowService?.Dispose(); };
        SettingsHost.Children.Add(captureStatus);
        var top = new CheckBox { Content = "Поверх приложений", IsChecked = settings.AlwaysOnTop };
        top.Click += (_, _) => { settings.AlwaysOnTop = top.IsChecked == true; ApplyWindowSettings(); };
        SettingsHost.Children.Add(top);
        var exclude = new CheckBox { Content = "Исключать окно из захвата", IsChecked = settings.ExcludeCapture };
        exclude.Click += (_, _) => { settings.ExcludeCapture = exclude.IsChecked == true; ApplyWindowSettings(); };
        SettingsHost.Children.Add(exclude);
        SettingsHost.Children.Add(new TextBlock { Text = "Горячие клавиши: Ctrl + Shift + выбранная клавиша", Margin = new(0, 20, 0, 8) });
        AddKeySetting("Показать / скрыть", settings.ToggleKey, v => settings.ToggleKey = v);
        AddKeySetting("Ответ по разговору", settings.AnswerKey, v => settings.AnswerKey = v);
        AddKeySetting("Скриншот", settings.ScreenshotKey, v => settings.ScreenshotKey = v);
        AddKeySetting("Скрыть и остановить запись", settings.PanicKey, v => settings.PanicKey = v);
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
        settings.Left = Left; settings.Top = Top; settings.Width = Width; settings.Height = compact ? expandedHeight : Height; settings.Opacity = vm.WindowOpacity;
        try { settingsStore.Save(settings); } catch { vm.Status = "Не удалось сохранить настройки."; }
    }
    private void HandleHotkey(int id)
    {
        if (id == 1) { if (IsVisible && WindowState != WindowState.Minimized) Hide(); else { Show(); WindowState = WindowState.Normal; Activate(); } }
        if (id == 2) OnAnswerRequested();
        if (id == 3) OnScreenshotRequested();
        if (id == 4) { OnPanic(); if (windowService?.CanHide == true) Hide(); else WindowState = WindowState.Minimized; }
    }
    partial void OnAnswerRequested();
    partial void OnScreenshotRequested();
    partial void OnPanic();
}
