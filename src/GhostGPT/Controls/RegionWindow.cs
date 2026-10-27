using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GhostGPT.Services;
namespace GhostGPT.Controls;
public sealed class RegionWindow : Window
{
    [StructLayout(LayoutKind.Sequential)] private struct PixelPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PixelPoint point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint value);
    private PixelPoint start;
    private Point localStart;
    private bool selecting;
    public BitmapSource? Selection { get; private set; }
    public RegionWindow(DesktopFrame frame, bool exclude)
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
        Background = new ImageBrush(frame.Image) { Stretch = Stretch.Fill }; Cursor = Cursors.Cross;
        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromArgb(75, 0, 0, 0)) }; Content = canvas;
        var help = new TextBlock { Text = "Выделите область • Esc — отмена", Background = Brushes.Black, Padding = new(12), FontSize = 18 };
        Canvas.SetLeft(help, 30); Canvas.SetTop(help, 30); canvas.Children.Add(help);
        var rect = new Rectangle { Stroke = Brushes.MediumPurple, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(30, 160, 130, 255)) }; canvas.Children.Add(rect);
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, new IntPtr(-1), frame.X, frame.Y, frame.Width, frame.Height, 0x0040);
            if (exclude && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) SetWindowDisplayAffinity(hwnd, 0x11);
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseLeftButtonDown += (_, e) => { GetCursorPos(out start); localStart = e.GetPosition(canvas); selecting = true; CaptureMouse(); };
        MouseMove += (_, e) => { if (!selecting) return; var p = e.GetPosition(canvas); Canvas.SetLeft(rect, Math.Min(localStart.X, p.X)); Canvas.SetTop(rect, Math.Min(localStart.Y, p.Y)); rect.Width = Math.Abs(p.X-localStart.X); rect.Height = Math.Abs(p.Y-localStart.Y); };
        MouseLeftButtonUp += (_, _) =>
        {
            if (!selecting) return; selecting = false; ReleaseMouseCapture(); GetCursorPos(out var end);
            int left = Math.Clamp(Math.Min(start.X, end.X) - frame.X, 0, frame.Width), top = Math.Clamp(Math.Min(start.Y, end.Y) - frame.Y, 0, frame.Height);
            int right = Math.Clamp(Math.Max(start.X, end.X) - frame.X, 0, frame.Width), bottom = Math.Clamp(Math.Max(start.Y, end.Y) - frame.Y, 0, frame.Height);
            if (right-left >= 8 && bottom-top >= 8) { Selection = new CroppedBitmap(frame.Image, new(left, top, right-left, bottom-top)); Selection.Freeze(); }
            Close();
        };
    }
}
