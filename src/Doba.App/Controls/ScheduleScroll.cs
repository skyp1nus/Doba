using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Doba.App.Controls;

/// <summary>
/// The Today / Day schedule aside's own scrolling (DECISIONS "Today schedule aside"): the design's 812 px timeline fits
/// at 1600×1000 and does not scroll; in a shorter window the aside scrolls, and when the schedule is first laid out — and
/// again on <see cref="Reset"/> (Day: another day is shown) — it is centred on the now-line, or without one on the
/// earliest block.
/// </summary>
internal sealed class ScheduleScroll
{
    private readonly ScrollViewer _scroller;
    private readonly ScheduleTimeline _schedule;

    /// <summary>The schedule has been placed for what it shows now.</summary>
    private bool _placed;

    public ScheduleScroll(ScrollViewer scroller, ScheduleTimeline schedule)
    {
        _scroller = scroller;
        _schedule = schedule;
        scroller.PropertyChanged += OnScrollerPropertyChanged;
    }

    /// <summary>The schedule now shows another day: place it again (now, or after its next layout).</summary>
    public void Reset()
    {
        _placed = false;
        TryPlace();
    }

    private void OnScrollerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.ViewportProperty || e.Property == ScrollViewer.ExtentProperty)
        {
            TryPlace();
        }
    }

    private void TryPlace()
    {
        if (_placed || _scroller.Viewport.Height <= 0 || _scroller.Extent.Height <= 0)
        {
            return;
        }

        _placed = true;
        if (_scroller.Extent.Height > _scroller.Viewport.Height)
        {
            // After the current layout pass, so the now-line and blocks have their final positions.
            Dispatcher.UIThread.Post(Centre, DispatcherPriority.Background);
        }
    }

    private void Centre()
    {
        Control? target = _schedule.NowLine.IsVisible ? _schedule.NowLine : _schedule.FirstBlock;
        var viewport = _scroller.Viewport.Height;
        var max = Math.Max(0, _scroller.Extent.Height - viewport);
        if (target?.TranslatePoint(new Point(0, target.Bounds.Height / 2), _schedule) is not { } centre)
        {
            _scroller.Offset = default; // nothing timed: the top of the grid
            return;
        }

        _scroller.Offset = new Vector(0, Math.Clamp(Math.Round(centre.Y - (viewport / 2)), 0, max));
    }
}
