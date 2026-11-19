using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Services;
using Doba.App.Shell;
using Doba.Core.Navigation;
using Doba.Core.Projections;
using Doba.Core.Store;

namespace Doba.App.Screens;

/// <summary>Everything a screen view-model needs, created once by the shell.</summary>
public sealed record ScreenServices(AppServices App, TaskActions Actions, IAppCommands Commands, Func<string?> FilterTag);

/// <summary>
/// Base for the Inbox, Today, Year, Month, Week, Day and Settings view-models. The shell keeps one instance per
/// screen and calls <see cref="Show"/> with the navigation state; the screen re-projects on store, settings, filter
/// and clock changes while it is active (<see cref="IsActive"/>), and only then. Re-projection must go through
/// <see cref="KeyedSync"/> so rows update in place.
/// </summary>
public abstract partial class ScreenViewModel : ObservableObject, IDisposable
{
    protected ScreenViewModel(ScreenServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
        services.App.Store.Changed += OnStoreChanged;
        services.App.Settings.Changed += OnSettingsChanged;
        services.App.DayClock.MinuteTick += OnMinuteTick;
    }

    protected ScreenServices Services { get; }

    protected ITaskStore Store => Services.App.Store;

    /// <summary>The navigation state last shown (anchor date, settings tab).</summary>
    public NavState? State { get; private set; }

    /// <summary>True while this screen is the visible one.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Current projection inputs: today/now from the DayClock, week start from settings, the tag filter.</summary>
    protected ProjectionContext Context => new(
        Services.App.DayClock.Today, Services.App.DayClock.Now, Services.App.Settings.Current.WeekStart, Services.FilterTag());

    /// <summary>Called by the shell when this screen becomes visible or its anchor changes.</summary>
    public void Show(NavState state)
    {
        State = state;
        IsActive = true;
        Refresh();
    }

    /// <summary>Called by the shell when another screen takes over.</summary>
    public void Hide() => IsActive = false;

    /// <summary>Re-projects and reconciles. Called on Show, store/settings/filter changes and day/minute ticks while active.</summary>
    public void RefreshIfActive()
    {
        if (IsActive)
        {
            Refresh();
        }
    }

    /// <summary>Run the projection for <see cref="State"/> and push it into the bound collections.</summary>
    protected abstract void Refresh();

    /// <summary>Override to react to the minute tick more cheaply than a full refresh (e.g. move only the now-line).</summary>
    protected virtual void OnMinute() => RefreshIfActive();

    public virtual void Dispose()
    {
        Services.App.Store.Changed -= OnStoreChanged;
        Services.App.Settings.Changed -= OnSettingsChanged;
        Services.App.DayClock.MinuteTick -= OnMinuteTick;
        GC.SuppressFinalize(this);
    }

    private void OnStoreChanged(object? sender, TaskStoreChangedEventArgs e) => RefreshIfActive();

    private void OnSettingsChanged(object? sender, Doba.Core.Model.AppSettings e) => RefreshIfActive();

    private void OnMinuteTick(object? sender, EventArgs e) => OnMinute();
}
