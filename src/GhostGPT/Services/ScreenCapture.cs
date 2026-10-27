using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
namespace GhostGPT.Services;
public record DesktopFrame(BitmapSource Image, int X, int Y, int Width, int Height);
public static class ScreenCapture
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr source, int sx, int sy, uint mode);
    public static DesktopFrame Capture()
    {
        int x = GetSystemMetrics(76), y = GetSystemMetrics(77), width = GetSystemMetrics(78), height = GetSystemMetrics(79);
        if (width <= 0 || height <= 0 || (long)width * height > 100_000_000) throw new ProviderException("Размер рабочего стола не поддерживается.");
        IntPtr dc = GetDC(IntPtr.Zero), memory = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(dc); bitmap = CreateCompatibleBitmap(dc, width, height);
            if (dc == IntPtr.Zero || memory == IntPtr.Zero || bitmap == IntPtr.Zero) throw new ProviderException("Не удалось выделить память для снимка.");
            old = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, width, height, dc, x, y, 0x40CC0020)) throw new ProviderException("Windows не разрешила захват экрана.");
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); source.Freeze();
            return new(source, x, y, width, height);
        }
        finally { if (old != IntPtr.Zero) SelectObject(memory, old); if (bitmap != IntPtr.Zero) DeleteObject(bitmap); if (memory != IntPtr.Zero) DeleteDC(memory); if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc); }
    }
    public static string Encode(BitmapSource image)
    {
        double scale = Math.Min(1, 2048d / Math.Max(image.PixelWidth, image.PixelHeight));
        if (scale < 1) image = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(scale, scale));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream(); encoder.Save(stream);
        if (stream.Length > 10 * 1024 * 1024) throw new ProviderException("Снимок слишком большой. Выберите меньшую область.");
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }
}
