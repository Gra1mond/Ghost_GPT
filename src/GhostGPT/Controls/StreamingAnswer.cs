using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GhostGPT.ViewModels;

namespace GhostGPT.Controls;

// Batch formatting independently of token arrival. Unloading releases the timer,
// and selection temporarily pauses replacement so users can copy partial answers.
public sealed class StreamingAnswer : StackPanel
{
    private readonly ChatMessage message;
    private readonly ContentControl body = new();
    private readonly TextBlock status = new()
    {
        FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(177, 183, 201)),
        Margin = new(0, 8, 0, 0)
    };
    private readonly DispatcherTimer timer;
    private string? rendered;

    public StreamingAnswer(ChatMessage message)
    {
        this.message = message;
        Children.Add(body);
        Children.Add(status);
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); timer.Start(); };
        Unloaded += (_, _) => timer.Stop();
        Refresh();
    }

    internal void Refresh()
    {
        if (rendered == message.Text || body.IsKeyboardFocusWithin || body.IsMouseCaptureWithin) return;
        rendered = message.Text;
        bool waiting = string.IsNullOrWhiteSpace(rendered);
        body.Content = waiting ? null : MarkdownView.Render(rendered);
        status.Text = waiting ? "Ожидаю ответ модели…" : "Ответ поступает…";
        status.ToolTip = "Статус передачи ответа; внутренние рассуждения модели не отображаются.";
    }
}
