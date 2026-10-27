using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
namespace GhostGPT.Controls;
public static class MarkdownView
{
    public static FrameworkElement Render(string text)
    {
        var panel = new StackPanel();
        foreach (var block in Markdown.Parse(text)) AddBlock(panel, block);
        return panel;
    }
    private static void AddBlock(StackPanel panel, Markdig.Syntax.Block block)
    {
        if (block is CodeBlock code)
        {
            string content = code.Lines.ToString();
            var box = new TextBox { Text = content, IsReadOnly = true, FontFamily = new("Cascadia Code, Consolas"), FontSize = 13, TextWrapping = TextWrapping.NoWrap, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 500, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var copy = new Button { Content = "Копировать код", HorizontalAlignment = HorizontalAlignment.Left };
            copy.Click += (_, _) => Copy(content);
            panel.Children.Add(copy); panel.Children.Add(box);
        }
        else if (block is LeafBlock leaf)
        {
            var tb = new TextBlock { Margin = new(0, 0, 0, 10), LineHeight = 23 };
            if (block is HeadingBlock h) { tb.FontSize = 25 - h.Level * 2; tb.FontWeight = FontWeights.SemiBold; }
            if (leaf.Inline is not null) AddInline(tb.Inlines, leaf.Inline);
            panel.Children.Add(tb);
        }
        else if (block is ContainerBlock container)
        {
            foreach (var child in container)
            {
                var inner = new StackPanel { Margin = new(14, 0, 0, 0) };
                if (child is ListItemBlock) inner.Children.Add(new TextBlock { Text = "•", Foreground = Brushes.MediumPurple });
                AddBlock(inner, child); panel.Children.Add(inner);
            }
        }
    }
    private static void AddInline(InlineCollection target, ContainerInline container)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal: target.Add(new Run(literal.Content.ToString())); break;
                case CodeInline code: target.Add(new Run(code.Content) { FontFamily = new("Consolas"), Background = new SolidColorBrush(Color.FromRgb(53, 47, 72)) }); break;
                case LineBreakInline: target.Add(new LineBreak()); break;
                case EmphasisInline emphasis:
                    Span span = emphasis.DelimiterCount >= 2 ? new Bold() : new Italic(); AddInline(span.Inlines, emphasis); target.Add(span); break;
                case ContainerInline nested: AddInline(target, nested); break;
            }
        }
    }
    public static void Copy(string text) { try { Clipboard.SetText(text); } catch { MessageBox.Show("Буфер обмена занят. Повторите копирование.", "Ghost GPT"); } }
}
