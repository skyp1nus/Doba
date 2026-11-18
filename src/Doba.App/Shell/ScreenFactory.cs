using Avalonia.Controls;
using Doba.App.Screens;
using Doba.App.Screens.Day;
using Doba.App.Screens.Inbox;
using Doba.App.Screens.Month;
using Doba.App.Screens.Settings;
using Doba.App.Screens.Today;
using Doba.App.Screens.Week;
using Doba.App.Screens.Year;
using Doba.Core.Navigation;

namespace Doba.App.Shell;

/// <summary>
/// Maps <see cref="AppView"/> → screen view-model and view-model → view with plain switch expressions (no reflection,
/// no type-name lookup), so NativeAOT sees every type.
/// </summary>
public static class ScreenFactory
{
    /// <summary>A new view-model for <paramref name="view"/>.</summary>
    public static ScreenViewModel Create(AppView view, ScreenServices services) => view switch
    {
        AppView.Inbox => new InboxViewModel(services),
        AppView.Today => new TodayViewModel(services),
        AppView.Year => new YearViewModel(services),
        AppView.Month => new MonthViewModel(services),
        AppView.Week => new WeekViewModel(services),
        AppView.Day => new DayViewModel(services),
        AppView.Settings => new SettingsViewModel(services),
        _ => throw new ArgumentOutOfRangeException(nameof(view), view, null),
    };

    /// <summary>A new view for <paramref name="screen"/> (DataContext not set).</summary>
    public static Control CreateView(ScreenViewModel screen) => screen switch
    {
        InboxViewModel => new InboxView(),
        TodayViewModel => new TodayView(),
        YearViewModel => new YearView(),
        MonthViewModel => new MonthView(),
        WeekViewModel => new WeekView(),
        DayViewModel => new DayView(),
        SettingsViewModel => new SettingsView(),
        _ => throw new ArgumentOutOfRangeException(nameof(screen), screen?.GetType().Name, null),
    };
}
