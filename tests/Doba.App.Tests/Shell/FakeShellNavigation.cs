using System.ComponentModel;
using Doba.App.Shell;
using Doba.Core.Navigation;
using Doba.Core.Store;

namespace Doba.App.Tests.Shell;

/// <summary>
/// A test double for the shell: records every call and applies it like MainViewModel would (Navigator rules, filter
/// toggle, collapse, AddTag into the store), raising PropertyChanged and Navigated. Set <see cref="Apply"/> to false
/// to only record.
/// </summary>
public sealed class FakeShellNavigation : IShellNavigation
{
    private readonly ITaskStore _store;
    private readonly DateOnly _today;

    public FakeShellNavigation(ITaskStore store, DateOnly today, NavState? start = null)
    {
        _store = store;
        _today = today;
        Nav = start ?? new NavState(AppView.Week, today);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<NavigatedEventArgs>? Navigated;

    /// <summary>Every call as "Method(argument)".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Raw texts passed to <see cref="AddTag"/>.</summary>
    public List<string> AddedTags { get; } = [];

    public bool Apply { get; set; } = true;

    public NavState Nav { get; private set; }

    public bool IsSidebarCollapsed { get; private set; }

    public string? FilterTag { get; private set; }

    /// <summary>Changes the state from the test as the shell would (raises PropertyChanged + Navigated).</summary>
    public void SetNav(NavState state) => Go(new NavResult(state, TransitionKind.Next));

    public void SetCollapsed(bool collapsed)
    {
        IsSidebarCollapsed = collapsed;
        Raise(nameof(IsSidebarCollapsed));
    }

    public void SetFilter(string? tag)
    {
        FilterTag = tag;
        Raise(nameof(FilterTag));
    }

    public void GoSection(AppView view)
    {
        Calls.Add($"GoSection({view})");
        if (Apply)
        {
            Go(Navigator.GoSection(Nav, view, view == AppView.Today ? _today : Nav.Anchor));
        }
    }

    public void GoCalendar()
    {
        Calls.Add("GoCalendar()");
        if (Apply)
        {
            Go(Navigator.GoCalendar(Nav));
        }
    }

    public void SetZoomLevel(AppView level) => Calls.Add($"SetZoomLevel({level})");

    public void Shift(int direction) => Calls.Add($"Shift({direction})");

    public void GoToday() => Calls.Add("GoToday()");

    public void OpenSettings()
    {
        Calls.Add("OpenSettings()");
        if (Apply)
        {
            Go(Navigator.OpenSettings(Nav));
        }
    }

    public void BackFromSettings()
    {
        Calls.Add("BackFromSettings()");
        if (Apply)
        {
            Go(Navigator.BackFromSettings(Nav, _today));
        }
    }

    public void SetSettingsTab(SettingsTab tab)
    {
        Calls.Add($"SetSettingsTab({tab})");
        if (Apply)
        {
            Go(Navigator.SetTab(Nav, tab));
        }
    }

    public void ToggleSidebar()
    {
        Calls.Add("ToggleSidebar()");
        if (Apply)
        {
            SetCollapsed(!IsSidebarCollapsed);
        }
    }

    public void ToggleTagFilter(string tag)
    {
        Calls.Add($"ToggleTagFilter({tag})");
        if (Apply)
        {
            SetFilter(FilterTag == tag ? null : tag);
        }
    }

    public string? AddTag(string raw)
    {
        Calls.Add($"AddTag({raw})");
        AddedTags.Add(raw);
        return Apply ? _store.AddTag(raw) : null;
    }

    public void OpenSearch() => Calls.Add("OpenSearch()");

    private void Go(NavResult result)
    {
        if (result.Transition == TransitionKind.None)
        {
            return;
        }

        var from = Nav;
        Nav = result.State;
        Raise(nameof(Nav));
        Navigated?.Invoke(this, new NavigatedEventArgs(from, Nav, result.Transition, null));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
