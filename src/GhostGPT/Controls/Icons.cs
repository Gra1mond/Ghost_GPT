using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace GhostGPT.Controls;
public static class Icons
{
    public static FrameworkElement Make(string name, string? label = null)
    {
        string data = name switch
        {
            "search" => "M16,16 L22,22 M18,10 A8,8 0 1 1 2,10 A8,8 0 1 1 18,10",
            "folder" => "M2,7 L2,5 Q2,3 4,3 L9,3 12,6 20,6 Q22,6 22,8 L22,10 M2,7 L4,20 19,20 23,9 8,9 6,7 Z",
            "compose" => "M14,4 L20,4 20,10 M20,4 L10,14 M11,4 L6,4 Q3,4 3,7 L3,18 Q3,21 6,21 L17,21 Q20,21 20,18 L20,13",
            "sidebar" => "M3,4 L21,4 21,20 3,20 Z M9,4 L9,20 M5,8 L7,8 M5,12 L7,12",
            "history" => "M4,7 A9,9 0 1 1 3,15 M4,2 L4,8 10,8 M12,7 L12,12 16,15",
            "chat" => "M3,3 L21,3 21,17 9,17 3,22 Z M7,8 L17,8 M7,12 L14,12",
            "audio" => "M3,10 L3,14 M7,6 L7,18 M12,2 L12,22 M17,6 L17,18 M21,10 L21,14",
            "settings" => "M3,6 L21,6 M3,12 L21,12 M3,18 L21,18 M8,3 L8,9 M16,9 L16,15 M9,15 L9,21",
            "plus" => "M12,4 L12,20 M4,12 L20,12",
            "close" => "M5,5 L19,19 M19,5 L5,19",
            "max" => "M3,9 L3,3 9,3 M15,3 L21,3 21,9 M21,15 L21,21 15,21 M9,21 L3,21 3,15",
            "back" => "M4,4 L20,4 20,15 M8,8 L16,8 16,20 4,20 4,8 Z M8,12 L12,16 M12,12 L8,16",
            "compact" => "M3,4 L21,4 21,20 3,20 Z M12,12 L18,12 18,17 12,17 Z",
            "min" => "M4,17 L20,17",
            "send" => "M12,20 L12,4 M5,11 L12,4 19,11",
            "stop" => "M5,5 L19,5 19,19 5,19 Z",
            _ => "M4,6 L20,6 20,20 4,20 Z M8,6 L8,3 16,3 16,6"
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Path { Data = Geometry.Parse(data), Stroke = new SolidColorBrush(Color.FromRgb(182,184,188)), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 18, Height = 18, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center });
        if (label is not null) panel.Children.Add(new TextBlock { Text = label, FontSize = 15, FontFamily = new("Segoe UI Variable Text, Segoe UI"), Margin = new(10,0,0,0), VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }
}
