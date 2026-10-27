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
        var capture = new Button { Content = Icons.Make("plus", "Снимок"), ToolTip = "Снимок всего рабочего стола (все мониторы)", Background = System.Windows.Media.Brushes.Transparent, Padding = new(6,6,6,6) };
        capture.Click += (_, _) => OnScreenshotRequested();
        composerTools.Children.Add(capture); Composer.Children.Insert(0, attachmentPanel);
        var region = new Button { Content = "Область", ToolTip = "Выбрать область экрана", Background = System.Windows.Media.Brushes.Transparent, Padding = new(6), FontSize = 12 };
        region.Click += (_, _) => _ = CaptureRegionAsync(true);
        composerTools.Children.Add(region);
    }
    partial void OnScreenshotRequested() => _ = CaptureRegionAsync();
    private async Task CaptureRegionAsync(bool selectRegion = false)
    {
        if (capturing) return; capturing = true; panicDuringCapture = false; bool wasVisible = IsVisible;
        try
        {
            Hide(); await Task.Delay(150); if (panicDuringCapture) return;
            var desktop = ScreenCapture.Capture();
            System.Windows.Media.Imaging.BitmapSource? image = desktop.Image;
            if (selectRegion)
            {
                regionWindow = new(desktop, settings.ExcludeCapture);
                regionWindow.ShowDialog(); image = regionWindow.Selection;
            }
            if (image is not null) { pendingImage = ScreenCapture.Encode(image); OnAttachmentChanged(); vm.Status = "Снимок прикреплён. Проверьте его и нажмите «Отправить»."; Navigate(ChatPage); wasVisible = true; }
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
