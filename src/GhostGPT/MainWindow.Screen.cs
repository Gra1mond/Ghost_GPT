using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using GhostGPT.Controls;
using GhostGPT.Services;
using GhostGPT.ViewModels;
namespace GhostGPT;
public partial class MainWindow
{
    private readonly StackPanel attachmentPanel = new();
    private bool capturing;
    private bool panicDuringCapture;
    private RegionWindow? regionWindow;
    private void InitializeScreenshots()
    {
        var capture = new Button { Content = "＋  Скриншот", ToolTip = "Прикрепить область экрана", Background = System.Windows.Media.Brushes.Transparent, Padding = new(6,6,6,6) };
        capture.Click += (_, _) => OnScreenshotRequested();
        composerTools.Children.Add(capture); Composer.Children.Insert(0, attachmentPanel);
    }
    partial void OnScreenshotRequested() => _ = CaptureRegionAsync();
    private async Task CaptureRegionAsync()
    {
        if (capturing) return; capturing = true; panicDuringCapture = false; bool wasVisible = IsVisible;
        try
        {
            Hide(); await Task.Delay(150); if (panicDuringCapture) return;
            regionWindow = new(ScreenCapture.Capture(), settings.ExcludeCapture);
            regionWindow.ShowDialog();
            if (regionWindow.Selection is { } image) { pendingImage = ScreenCapture.Encode(image); OnAttachmentChanged(); vm.Status = "Снимок прикреплён. Проверьте его и нажмите «Отправить»."; Navigate(ChatPage); wasVisible = true; }
        }
        catch (Exception ex) { vm.Status = MainViewModel.FriendlyError(ex); }
        finally { regionWindow = null; capturing = false; if (wasVisible && !panicDuringCapture) { Show(); Activate(); } }
    }
    partial void OnAttachmentChanged()
    {
        attachmentPanel.Children.Clear(); if (pendingImage is null) return;
        using var stream = new MemoryStream(Convert.FromBase64String(pendingImage.Split(',')[1]));
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        attachmentPanel.Children.Add(new Image { Source = bitmap, MaxHeight = 110, HorizontalAlignment = HorizontalAlignment.Left });
        var remove = new Button { Content = "Удалить снимок", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 6, 0, 8) };
        remove.Click += (_, _) => { pendingImage = null; OnAttachmentChanged(); }; attachmentPanel.Children.Add(remove);
    }
}
