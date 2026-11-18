using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Screens;
using Doba.App.Services;
using Doba.App.Shell;
using Doba.Core.Projections;
using Doba.Core.Store;

namespace Doba.App.Dialogs.Search;

/// <summary>
/// The Ctrl+K search palette: query box and hits from the Core search projection (<see cref="Projector.Search"/>:
/// case-insensitive substring on titles across all tasks, open first, at most <see cref="Limit"/>). Every query change
/// re-runs the projection and reconciles <see cref="Hits"/> in place; the first hit is then selected. ↑/↓ move the
/// selection (clamped). Picking a hit always opens that task's edit dialog (<c>IAppCommands.EditTask</c>, which
/// replaces this overlay); the palette never navigates and never changes a task.
/// </summary>
public sealed partial class SearchPaletteViewModel : OverlayViewModel
{
    /// <summary>Most hits listed (the projection's cap; the list scrolls past eight rows).</summary>
    public const int Limit = 50;

    private bool _closed;

    public SearchPaletteViewModel(ScreenServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
        services.App.Store.Changed += OnStoreChanged;
    }

    /// <summary>Store, settings, clock and shell commands.</summary>
    public ScreenServices Services { get; }

    /// <summary>The hits in display order (empty for an empty query).</summary>
    public ObservableCollection<SearchHitViewModel> Hits { get; } = [];

    /// <summary>The text in the query box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery), nameof(ShowsNoResults))]
    public partial string Query { get; set; } = string.Empty;

    /// <summary>Index of the selected hit in <see cref="Hits"/>; -1 when there are none.</summary>
    [ObservableProperty] public partial int SelectedIndex { get; private set; } = -1;

    /// <summary>The query has something besides white space (the result area is shown).</summary>
    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    /// <summary>At least one hit.</summary>
    public bool HasHits => Hits.Count > 0;

    /// <summary>A query without hits: the muted "No tasks found" line.</summary>
    public bool ShowsNoResults => HasQuery && Hits.Count == 0;

    /// <summary>The selected hit, or null.</summary>
    public SearchHitViewModel? Selected => SelectedIndex >= 0 && SelectedIndex < Hits.Count ? Hits[SelectedIndex] : null;

    /// <summary>↑ / ↓: moves the selection by <paramref name="delta"/>, clamped to the list (no wrap).</summary>
    public void MoveSelection(int delta)
    {
        if (Hits.Count == 0)
        {
            return;
        }

        Select(Math.Clamp(SelectedIndex + delta, 0, Hits.Count - 1));
    }

    /// <summary>Enter: opens the selected hit's task; nothing when there is none.</summary>
    public void OpenSelected()
    {
        if (Selected is { } hit)
        {
            Open(hit);
        }
    }

    /// <summary>Opens <paramref name="hit"/>'s edit dialog in place of the palette.</summary>
    public void Open(SearchHitViewModel hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (!_closed)
        {
            Services.Commands.EditTask(hit.Id);
        }
    }

    public override void OnClosed()
    {
        _closed = true;
        Services.App.Store.Changed -= OnStoreChanged;
    }

    partial void OnQueryChanged(string value) => Refresh(keepSelection: false);

    private void OnStoreChanged(object? sender, TaskStoreChangedEventArgs e) => Refresh(keepSelection: true);

    /// <summary>Re-runs the projection and reconciles the rows. A query change selects the first hit; a store change
    /// keeps the selected task selected while it still matches.</summary>
    private void Refresh(bool keepSelection)
    {
        if (_closed)
        {
            return;
        }

        var app = Services.App;
        var ctx = new ProjectionContext(app.DayClock.Today, app.DayClock.Now, app.Settings.Current.WeekStart, Services.FilterTag());
        var hits = Projector.Search(app.Store, ctx, Query, Limit);
        var selectedId = keepSelection ? Selected?.Id : null;

        KeyedSync.Apply(Hits, hits, static h => h.Row.Id, static vm => vm.Id, h => new SearchHitViewModel(h, Open), static (vm, h) => vm.Update(h));

        var index = 0;
        if (selectedId is { } id)
        {
            index = Math.Min(Math.Max(SelectedIndex, 0), Hits.Count - 1);
            for (var i = 0; i < Hits.Count; i++)
            {
                if (Hits[i].Id == id)
                {
                    index = i;
                    break;
                }
            }
        }

        Select(Hits.Count == 0 ? -1 : index, force: true);
        OnPropertyChanged(nameof(HasHits));
        OnPropertyChanged(nameof(ShowsNoResults));
    }

    private void Select(int index, bool force = false)
    {
        for (var i = 0; i < Hits.Count; i++)
        {
            Hits[i].IsSelected = i == index;
        }

        if (SelectedIndex != index)
        {
            SelectedIndex = index;
            OnPropertyChanged(nameof(Selected));
        }
        else if (force)
        {
            // Same index, maybe another hit under it (or the list was scrolled): let the view bring it into view again.
            OnPropertyChanged(nameof(SelectedIndex));
            OnPropertyChanged(nameof(Selected));
        }
    }
}
