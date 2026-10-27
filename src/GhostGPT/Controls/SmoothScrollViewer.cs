using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace GhostGPT.Controls;

/// <summary>Pixel scrolling with a bounded, interruptible easing interval.</summary>
public sealed class SmoothScrollViewer : ScrollViewer
{
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = new();
    private double target;
    private bool applying, followingEnd = true;
    public bool FollowNewContent { get; set; }
    public void JumpToLatest() { Stop(); followingEnd = true; Apply(ScrollableHeight); }

    public SmoothScrollViewer()
    {
        CanContentScroll = false;
        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => Advance();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => { if (!IsVisible) Stop(); };
        PreviewMouseDown += (_, _) => Stop(); // Scrollbar dragging takes over immediately.
        PreviewKeyDown += (_, _) => Stop();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None || ScrollableHeight <= 0) { base.OnMouseWheel(e); return; }
        int lines = SystemParameters.WheelScrollLines;
        if (lines == 0) { e.Handled = true; return; }
        double step = lines < 0 ? ViewportHeight : lines * 18;
        double origin = timer.IsEnabled ? target : VerticalOffset;
        double next = Math.Clamp(origin - e.Delta / 120d * step, 0, ScrollableHeight);
        // Allow an outer viewer to scroll once this viewer has reached its edge.
        if (Math.Abs(next - VerticalOffset) < .5 && Math.Abs(next - origin) < .5) return;
        followingEnd = e.Delta < 0 && next >= ScrollableHeight - 1;
        MoveTo(next);
        e.Handled = true;
    }

    protected override void OnScrollChanged(ScrollChangedEventArgs e)
    {
        base.OnScrollChanged(e);
        if (e.OriginalSource != this) return;
        if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
        {
            if (FollowNewContent && followingEnd) MoveTo(ScrollableHeight);
            else target = Math.Clamp(target, 0, ScrollableHeight);
        }
        else if (!applying && e.VerticalChange != 0)
        {
            Stop();
            followingEnd = ScrollableHeight - VerticalOffset < 2;
        }
    }

    private void MoveTo(double offset)
    {
        target = Math.Clamp(offset, 0, ScrollableHeight);
        if (!SystemParameters.ClientAreaAnimation) { Apply(target); return; }
        if (!timer.IsEnabled) { clock.Restart(); timer.Start(); }
    }

    private void Advance()
    {
        double seconds = clock.Elapsed.TotalSeconds; clock.Restart();
        target = Math.Clamp(target, 0, ScrollableHeight);
        double remaining = target - VerticalOffset;
        if (Math.Abs(remaining) < .5) { Apply(target); timer.Stop(); return; }
        Apply(VerticalOffset + remaining * (1 - Math.Exp(-seconds / .055)));
    }

    private void Apply(double offset)
    {
        applying = true;
        try { ScrollToVerticalOffset(offset); UpdateLayout(); }
        finally { applying = false; }
    }

    private void Stop() { timer.Stop(); clock.Reset(); target = VerticalOffset; }
}
