using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
namespace GhostGPT;
public partial class MainWindow
{
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private bool sentBehind, fullscreen;
    private Rect beforeFullscreen;
    private WindowState beforeFullscreenState;
    private void InitializeWorkspaceControls()
    {
        Icon = Controls.GhostVisual.Artwork();
        BackButton.Content = Controls.Icons.Make("back"); MaxButton.Content = Controls.Icons.Make("max");
        CompactButton.Content = Controls.Icons.Make("compact"); HideButton.Content = Controls.Icons.Make("min"); ExitButton.Content = Controls.Icons.Make("close");
        SendButton.Content = Controls.Icons.Make("send");
        var transparency = new Slider { Minimum = 0, Maximum = .85, Width = 100, Margin = new(10,0,10,0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Прозрачность фона" };
        transparency.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding(nameof(vm.BackgroundTransparency)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        composerTools.Children.Add(transparency);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && FolderCreatePanel.Visibility == Visibility.Visible) { CancelFolderCreate(this,e); e.Handled = true; return; }
            if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
            else if (e.Key == Key.Escape && fullscreen) { ToggleFullscreen(); e.Handled = true; }
        };
        UpdateBehindTooltip();
    }
    private void UpdateBehindTooltip() => BackButton.ToolTip = $"За другие окна · Ctrl+Shift+{settings.SendBehindKey}";
    private void SendBehind(object sender, RoutedEventArgs e)
    {
        sentBehind = true; Topmost = false;
        SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(1), 0,0,0,0, 0x13);
        vm.Status = $"Окно за приложениями · Ctrl+Shift+{settings.ToggleKey} — вернуть";
    }
    private void BringForward()
    {
        sentBehind = false; Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Topmost = true; Activate(); PromptBox.Focus();
    }
    private void ToggleMaximize(object sender, RoutedEventArgs e)
    {
        if (fullscreen) ToggleFullscreen();
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    private void ToggleFullscreen()
    {
        if (!fullscreen)
        {
            beforeFullscreenState = WindowState;
            beforeFullscreen = WindowState == WindowState.Normal ? new Rect(Left,Top,Width,Height) : RestoreBounds;
            WindowState = WindowState.Normal;
            var area = windowService?.MonitorBounds(false) ?? new Rect(0,0,SystemParameters.PrimaryScreenWidth,SystemParameters.PrimaryScreenHeight);
            Left = area.Left; Top = area.Top; Width = area.Width; Height = area.Height; fullscreen = true;
        }
        else
        {
            fullscreen = false; Left = beforeFullscreen.Left; Top = beforeFullscreen.Top; Width = beforeFullscreen.Width; Height = beforeFullscreen.Height; WindowState = beforeFullscreenState;
        }
        WindowSurface.CornerRadius = new(fullscreen ? 0 : 16);
    }
}
