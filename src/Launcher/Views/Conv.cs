using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DieReanimated.Launcher.Ui.Views;

/// <summary>The few bool→brush switches the screens need. Colours match Theme.axaml.</summary>
public static class Conv
{
    private static readonly IBrush Green = Brush.Parse("#54a438");
    private static readonly IBrush Off = Brush.Parse("#be2020");
    private static readonly IBrush Amber = Brush.Parse("#c47e00");
    private static readonly IBrush Red = Brush.Parse("#c41800");
    private static readonly IBrush Muted = Brush.Parse("#8a9a93");
    private static readonly IBrush Teal = Brush.Parse("#4e9882");

    public static readonly IValueConverter GreenOrOff = new FuncValueConverter<bool, IBrush>(b => b ? Green : Off);
    public static readonly IValueConverter MutedOrAmber = new FuncValueConverter<bool, IBrush>(b => b ? Muted : Amber);
    public static readonly IValueConverter RedOrMuted = new FuncValueConverter<bool, IBrush>(b => b ? Red : Muted);
    public static readonly IValueConverter OnlineText = new FuncValueConverter<bool, string>(b => b ? "ONLINE" : "OFFLINE");
    /// <summary>A 0–1 fraction as a width: parameter × fraction pixels (the bar under the primary button).</summary>
    public static readonly IValueConverter Fraction = new FuncValueConverter<double, object?, double>((f, p) =>
        (p is string s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double w) ? w : 200) * Math.Clamp(f, 0, 1));
    /// <summary>News kind colour: patch notes red, events amber, the rest teal.</summary>
    public static readonly IValueConverter KindBrush = new FuncValueConverter<string?, IBrush>(k => k switch
    {
        "PATCH NOTES" => Red,
        "EVENT" => Amber,
        _ => Teal,
    });
}
