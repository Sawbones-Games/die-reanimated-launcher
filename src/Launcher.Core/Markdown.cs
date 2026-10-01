using System.Text.RegularExpressions;

namespace DieReanimated.Launcher;

/// <summary>
/// The subset of Markdown a news body may use — enough for patch notes, small enough to render natively:
/// <c>##</c>/<c>###</c> headings, paragraphs, <c>-</c>/<c>*</c> bullet lists, and inline <c>**bold**</c>,
/// <c>*italic*</c>, <c>`code`</c>. Anything else is text. No HTML, no links, no images: a body cannot make the
/// launcher fetch or open anything.
/// </summary>
public static class Markdown
{
    public enum BlockKind { Heading, Paragraph, Bullet }
    public sealed record Span(string Text, bool Bold, bool Italic, bool Code);
    public sealed record Block(BlockKind Kind, IReadOnlyList<Span> Spans, int Level = 0);

    public static IReadOnlyList<Block> Parse(string text)
    {
        var blocks = new List<Block>();
        var para = new List<string>();
        void FlushParagraph()
        {
            if (para.Count == 0) return;
            blocks.Add(new Block(BlockKind.Paragraph, Inline(string.Join(" ", para))));
            para.Clear();
        }
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Length == 0) { FlushParagraph(); continue; }
            var h = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (h.Success) { FlushParagraph(); blocks.Add(new Block(BlockKind.Heading, Inline(h.Groups[2].Value), Math.Min(h.Groups[1].Length, 3))); continue; }
            var b = Regex.Match(line, @"^\s*[-*•]\s+(.*)$");
            if (b.Success) { FlushParagraph(); blocks.Add(new Block(BlockKind.Bullet, Inline(b.Groups[1].Value))); continue; }
            para.Add(line.Trim());
        }
        FlushParagraph();
        return blocks;
    }

    /// <summary>Inline marks, left to right; unbalanced marks are literal text.</summary>
    public static IReadOnlyList<Span> Inline(string s)
    {
        var spans = new List<Span>();
        var m = Regex.Matches(s, @"`([^`]+)`|\*\*(.+?)\*\*|\*([^*]+)\*");
        int at = 0;
        foreach (Match x in m)
        {
            if (x.Index > at) spans.Add(new Span(s[at..x.Index], false, false, false));
            if (x.Groups[1].Success) spans.Add(new Span(x.Groups[1].Value, false, false, true));
            else if (x.Groups[2].Success) spans.Add(new Span(x.Groups[2].Value, true, false, false));
            else spans.Add(new Span(x.Groups[3].Value, false, true, false));
            at = x.Index + x.Length;
        }
        if (at < s.Length) spans.Add(new Span(s[at..], false, false, false));
        return spans;
    }
}
