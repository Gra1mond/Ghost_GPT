namespace GhostGPT.ViewModels;
public partial class MainViewModel : Observable
{
    private string status = "Готов к работе · настройте подключение";
    public string Status { get => status; set => Set(ref status, value); }
    private double backgroundOpacity = 0.96;
    public double BackgroundOpacity
    {
        get => backgroundOpacity;
        set { if (Set(ref backgroundOpacity, double.IsFinite(value) ? Math.Clamp(value, .15, 1) : .96)) { Changed(nameof(WindowBackground)); Changed(nameof(PanelBackground)); Changed(nameof(BackgroundTransparency)); } }
    }
    public double BackgroundTransparency { get => 1 - backgroundOpacity; set => BackgroundOpacity = 1 - value; }
    public System.Windows.Media.SolidColorBrush WindowBackground => BackgroundBrush(backgroundOpacity, 33);
    public System.Windows.Media.SolidColorBrush PanelBackground => BackgroundBrush(Math.Min(1, backgroundOpacity + .12), 48);
    private static System.Windows.Media.SolidColorBrush BackgroundBrush(double opacity, byte shade)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)Math.Round(opacity * 255), shade, shade, shade));
        brush.Freeze(); return brush;
    }
}
