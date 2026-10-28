using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
namespace GhostGPT.Controls;
public static class GhostVisual
{
    public static DrawingImage Artwork(bool badge = true)
    {
        var group = new DrawingGroup();
        if (badge)
        {
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(32,33,38)), null, new EllipseGeometry(new Point(32,32),31,31)));
            group.Children.Add(new GeometryDrawing(null,new Pen(new SolidColorBrush(Color.FromRgb(226,65,73)),5),new EllipseGeometry(new Point(32,32),27,27)));
        }
        var outline = Geometry.Parse("M18,31 C16,12 23,8 32,8 C44,8 49,17 46,30 L54,24 Q59,22 57,29 L50,40 Q47,44 45,42 L46,53 Q43,58 38,51 Q33,61 28,52 Q22,58 19,52 L19,42 Q14,44 10,38 L5,28 Q4,22 10,25 Z");
        group.Children.Add(new GeometryDrawing(badge ? Brushes.White : new LinearGradientBrush(Color.FromRgb(255,255,255), Color.FromRgb(180,190,226), 70),new Pen(new SolidColorBrush(Color.FromRgb(28,29,35)),1.4),outline));
        group.Children.Add(new GeometryDrawing(Brushes.Black,null,new EllipseGeometry(new Point(27,24),2.7,4)));
        group.Children.Add(new GeometryDrawing(Brushes.Black,null,new EllipseGeometry(new Point(38,24),2.7,4)));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(44,35,49)),null,Geometry.Parse("M26,34 Q32,30 39,34 Q37,45 32,44 Q27,42 26,34 Z")));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(238,128,153)),null,new EllipseGeometry(new Point(33,41),3,1.5)));
        var image = new DrawingImage(group); image.Freeze(); return image;
    }
    public static Image Create(double size = 28)
    {
        var transforms = new TransformGroup();
        transforms.Children.Add(new ScaleTransform(1, 1));
        transforms.Children.Add(new RotateTransform());
        transforms.Children.Add(new TranslateTransform());
        return new Image { Source = Artwork(false), Width = size, Height = size,
            RenderTransformOrigin = new(.5, .5), RenderTransform = transforms,
            Margin = new(6, 6, 16, 6), ToolTip = "Ghost" };
    }
    public static void Animate(Image image, bool active)
    {
        if (image.RenderTransform is not TransformGroup group) return;
        bool moving = active && SystemParameters.ClientAreaAnimation;
        var scale = (ScaleTransform)group.Children[0];
        var rotation = (RotateTransform)group.Children[1];
        var position = (TranslateTransform)group.Children[2];
        // Transform clocks avoid layout and bitmap redraw on each animation frame.
        DoubleAnimation? Float(double from, double to, int milliseconds) => moving
            ? new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds)) {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } } : null;
        position.BeginAnimation(TranslateTransform.XProperty, Float(-2, 2, 1100));
        position.BeginAnimation(TranslateTransform.YProperty, Float(2, -4, 800));
        rotation.BeginAnimation(RotateTransform.AngleProperty, Float(-7, 7, 1100));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Float(.97, 1.03, 800));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Float(1.03, .97, 800));
        image.ToolTip = active ? "Ghost готовит ответ…" : "Ghost";
    }
    public static void ExportIcon(string path)
    {
        int[] sizes = [16,32,48,64,128,256]; var images = new List<byte[]>();
        foreach (int size in sizes)
        {
            var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawImage(Artwork(),new Rect(0,0,size,size));
            var bitmap = new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream(); encoder.Save(stream); images.Add(stream.ToArray());
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new BinaryWriter(File.Create(path)); writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
        int offset = 6 + sizes.Length * 16;
        for (int i=0;i<sizes.Length;i++) { writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length; }
        foreach (var bytes in images) writer.Write(bytes);
    }
}
