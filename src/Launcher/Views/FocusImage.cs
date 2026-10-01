using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DieReanimated.Launcher.Ui.Views;

/// <summary>An image that fills its box (like <c>Stretch=UniformToFill</c>) but crops around a focal point instead of
/// the centre: <see cref="FocalPoint"/> is where the picture's interest is, 0–1 from the top-left, and it stays in view
/// whatever the box's shape. A 16:9 slot showing a portrait's face rather than its chest.</summary>
public sealed class FocusImage : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty = AvaloniaProperty.Register<FocusImage, Bitmap?>(nameof(Source));
    public static readonly StyledProperty<Point> FocalPointProperty = AvaloniaProperty.Register<FocusImage, Point>(nameof(FocalPoint), new Point(0.5, 0.5));

    public static readonly Point Centre = new(0.5, 0.5);
    public static readonly Point Right = new(0.65, 0.45);
    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public Point FocalPoint { get => GetValue(FocalPointProperty); set => SetValue(FocalPointProperty, value); }

    static FocusImage()
    {
        AffectsRender<FocusImage>(SourceProperty, FocalPointProperty);
        ClipToBoundsProperty.OverrideDefaultValue<FocusImage>(true);
    }

    public override void Render(DrawingContext context)
    {
        var src = Source;
        if (src is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        double sw = src.PixelSize.Width, sh = src.PixelSize.Height;
        double scale = Math.Max(Bounds.Width / sw, Bounds.Height / sh);
        double cw = Bounds.Width / scale, ch = Bounds.Height / scale;          // the source window that fills the box
        double cx = Math.Clamp(FocalPoint.X * sw - cw / 2, 0, sw - cw);
        double cy = Math.Clamp(FocalPoint.Y * sh - ch / 2, 0, sh - ch);
        context.DrawImage(src, new Rect(cx, cy, cw, ch), new Rect(0, 0, Bounds.Width, Bounds.Height));
    }
}
