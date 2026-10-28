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
            var copy = new Button { Content = "Копировать код", HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent, Padding = new(10,6,10,6), FontSize = 11 };
            copy.Click += (_, _) => Copy(content);
            box.BorderThickness = new(0); box.Background = Brushes.Transparent; box.Margin = new(0);
            var toolbar = new DockPanel { LastChildFill = true, Margin = new(12,6,8,6) };
            DockPanel.SetDock(copy,Dock.Right); toolbar.Children.Add(copy);
            var wrap = new Button { Content = "Перенос", ToolTip = "Перенос длинных строк", Background = Brushes.Transparent, FontSize = 11, Padding = new(8,6,8,6) };
            wrap.Click += (_, _) => { bool enabled = box.TextWrapping == TextWrapping.NoWrap; box.TextWrapping = enabled ? TextWrapping.Wrap : TextWrapping.NoWrap; box.HorizontalScrollBarVisibility = enabled ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto; wrap.Content = enabled ? "Без переноса" : "Перенос"; };
            DockPanel.SetDock(wrap,Dock.Right); toolbar.Children.Add(wrap);
            string language = code is FencedCodeBlock fenced && !string.IsNullOrWhiteSpace(fenced.Info) ? fenced.Info : "Код";
            toolbar.Children.Add(new TextBlock { Text = language, FontFamily = new("Consolas"), FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(196,179,240)), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
            var body = new StackPanel(); body.Children.Add(new Border { Child = toolbar, Background = new SolidColorBrush(Color.FromArgb(55,115,100,145)), CornerRadius = new(11,11,0,0), BorderBrush = new SolidColorBrush(Color.FromArgb(45,180,170,210)), BorderThickness = new(0,0,0,1) }); body.Children.Add(box);
            box.Padding = new(16); box.Foreground = new SolidColorBrush(Color.FromRgb(220,224,239));
            panel.Children.Add(new Border { Child = body, Background = new SolidColorBrush(Color.FromArgb(150,12,12,18)), BorderBrush = new SolidColorBrush(Color.FromArgb(65,180,170,210)), BorderThickness = new(1), CornerRadius = new(12), Margin = new(0,4,0,16) });
        }
        else if (block is ListBlock list)
        {
            int number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
            foreach (var item in list)
            {
                var row = new Grid { Margin = new(0,2,0,6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(new TextBlock { Text = list.IsOrdered ? $"{number++}." : "•", Foreground = Brushes.MediumPurple, LineHeight = 25 });
                var text = new StackPanel(); Grid.SetColumn(text,1); row.Children.Add(text);
                if (item is ContainerBlock items) foreach (var child in items) AddBlock(text,child);
                panel.Children.Add(row);
            }
        }
        else if (block is QuoteBlock quote)
        {
            var body = new StackPanel(); foreach (var child in quote) AddBlock(body,child);
            panel.Children.Add(new Border { Child = body, BorderBrush = Brushes.MediumPurple, BorderThickness = new(3,0,0,0), Padding = new(14,6,8,0), Margin = new(0,4,0,14) });
        }
        else if (block is LeafBlock leaf)
        {
            var paragraph = new Paragraph { Margin = new(0), LineHeight = 25 };
            if (block is HeadingBlock h) { paragraph.FontSize = 25 - h.Level * 2; paragraph.FontWeight = FontWeights.SemiBold; }
            if (leaf.Inline is not null) AddInline(paragraph.Inlines, leaf.Inline);
            var document = new FlowDocument(paragraph) { PagePadding = new(0), FontFamily = new("Segoe UI Variable Text, Segoe UI"), FontSize = 15, Foreground = Brushes.Gainsboro };
            var selectable = new RichTextBox { Document = document, IsReadOnly = true, IsDocumentEnabled = true, Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(0), Margin = new(0,0,0,14), VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, SelectionBrush = Brushes.MediumPurple };
            var menu = new ContextMenu { Background = new SolidColorBrush(Color.FromRgb(40,40,44)), Foreground = Brushes.Gainsboro };
            menu.Items.Add(new MenuItem { Header = "Копировать выделенное", Command = System.Windows.Input.ApplicationCommands.Copy, CommandTarget = selectable });
            selectable.ContextMenu = menu;
            panel.Children.Add(selectable);
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
                case LineBreakInline line: if (line.IsHard) target.Add(new LineBreak()); else target.Add(new Run(" ")); break;
                case EmphasisInline emphasis:
                    Span span = emphasis.DelimiterCount >= 2 ? new Bold() : new Italic(); AddInline(span.Inlines, emphasis); target.Add(span); break;
                case ContainerInline nested: AddInline(target, nested); break;
            }
        }
    }
    public static void Copy(string text) { try { Clipboard.SetText(text); } catch { MessageBox.Show("Буфер обмена занят. Повторите копирование.", "Ghost GPT"); } }
}
