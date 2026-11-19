using Avalonia.Threading;
using Doba.Core.Time;

namespace Doba.App.Services;

/// <summary>
/// Publishes "now" to the UI: <see cref="MinuteTick"/> once a minute (now-line, labels) and <see cref="DayChanged"/>
/// when the local date changes while the app is open (midnight rollover: re-project, carry over, daily backup).
/// Time always comes from the injected <see cref="TimeProvider"/>. <see cref="Check"/> is public so tests can drive it.
/// </summary>
public sealed class DayClock : IDisposable
{
    private readonly TimeProvider _clock;
    private DispatcherTimer? _timer;

    public DayClock(TimeProvider clock)
    {
        _clock = clock;
        Today = clock.Today();
        Now = TruncateToMinute(clock.TimeOfDay());
    }

    public DateOnly Today { get; private set; }

    public TimeOnly Now { get; private set; }

    /// <summary>Raised when the minute changes (and also after a day change).</summary>
    public event EventHandler? MinuteTick;

    /// <summary>Raised with the new date when the local day changes.</summary>
    public event EventHandler<DateOnly>? DayChanged;

    /// <summary>Starts a UI-thread timer that wakes shortly after each minute boundary.</summary>
    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick += (_, _) => { Check(); ScheduleNext(); };
        ScheduleNext();
    }

    /// <summary>Re-reads the clock and raises events for whatever changed.</summary>
    public void Check()
    {
        var today = _clock.Today();
        var now = TruncateToMinute(_clock.TimeOfDay());
        var dayChanged = today != Today;
        var minuteChanged = dayChanged || now != Now;
        Today = today;
        Now = now;
        if (dayChanged)
        {
            DayChanged?.Invoke(this, today);
        }

        if (minuteChanged)
        {
            MinuteTick?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void ScheduleNext()
    {
        if (_timer is null)
        {
            return;
        }

        var t = _clock.TimeOfDay();
        var untilNextMinute = TimeSpan.FromSeconds(60 - t.Second) - TimeSpan.FromMilliseconds(t.Millisecond);
        _timer.Stop();
        _timer.Interval = untilNextMinute + TimeSpan.FromMilliseconds(50);
        _timer.Start();
    }

    private static TimeOnly TruncateToMinute(TimeOnly t) => new(t.Hour, t.Minute);
}
