using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using DieReanimated.Launcher;

namespace DieReanimated.Launcher.Ui.Views;

/// <summary>Renders <see cref="Markdown"/> blocks in the launcher's own type: `##` as a red-bar section heading,
/// paragraphs as reading text, bullets with a hanging indent. Nothing here is interactive.</summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<IReadOnlyList<Markdown.Block>?> BlocksProperty =
        AvaloniaProperty.Register<MarkdownView, IReadOnlyList<Markdown.Block>?>(nameof(Blocks));
    public IReadOnlyList<Markdown.Block>? Blocks { get => GetValue(BlocksProperty); set => SetValue(BlocksProperty, value); }

    public MarkdownView() { Spacing = 5; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BlocksProperty) Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        if (Blocks is null) return;
        var bar = this.FindResource("B.RedDeep") as IBrush;
        var body = this.FindResource("B.Body") as IBrush;
        var bone = this.FindResource("B.Bone") as IBrush;
        var muted = this.FindResource("B.Bone2") as IBrush;
        var label = this.FindResource("FontLabel") as FontFamily ?? FontFamily.Default;
        var text = this.FindResource("FontBody") as FontFamily ?? FontFamily.Default;

        foreach (var b in Blocks)
        {
            switch (b.Kind)
            {
                case Markdown.BlockKind.Heading:
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, Children.Count == 0 ? 0 : 8, 0, 2) };
                    row.Children.Add(new Border { Width = 3, Height = 14, Background = bar, VerticalAlignment = VerticalAlignment.Center });
                    var tb = new TextBlock { FontFamily = label, FontWeight = FontWeight.Bold, FontSize = b.Level >= 3 ? 10 : 11, LetterSpacing = 2, Foreground = bone, VerticalAlignment = VerticalAlignment.Center };
                    tb.Inlines!.Add(new Run(string.Concat(b.Spans.Select(s => s.Text)).ToUpperInvariant()));
                    row.Children.Add(tb);
                    Children.Add(row);
                    break;
                }
                case Markdown.BlockKind.Bullet:
                {
                    var g = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*") };
                    g.Children.Add(new TextBlock { Text = "•", Foreground = bar, FontSize = 13.5, LineHeight = 20 });
                    var tb = Text(b, text, muted);
                    Grid.SetColumn(tb, 1);
                    g.Children.Add(tb);
                    Children.Add(g);
                    break;
                }
                default:
                    var p = Text(b, text, body);
                    p.Margin = new Thickness(0, 0, 0, 3);
                    Children.Add(p);
                    break;
            }
        }
    }

    private static TextBlock Text(Markdown.Block b, FontFamily font, IBrush? brush)
    {
        var tb = new TextBlock { FontFamily = font, FontSize = 13.5, LineHeight = 20, Foreground = brush, TextWrapping = TextWrapping.Wrap };
        foreach (var s in b.Spans)
        {
            var run = new Run(s.Text);
            if (s.Bold) run.FontWeight = FontWeight.SemiBold;
            if (s.Italic) run.FontStyle = FontStyle.Italic;
            if (s.Code) { run.FontFamily = new FontFamily("Consolas, Courier New, monospace"); run.FontSize = 12.5; }
            tb.Inlines!.Add(run);
        }
        return tb;
    }
}
