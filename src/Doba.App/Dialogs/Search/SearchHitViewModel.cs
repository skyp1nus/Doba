using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.App.Services;
using Doba.Core.Model;
using Doba.Core.Projections;

namespace Doba.App.Dialogs.Search;

/// <summary>
/// One row of the search palette. Long-lived: <see cref="KeyedSync"/> pushes each new <see cref="SearchHit"/> for the
/// same task into it with <see cref="Update"/>, so typing narrows the list without rebuilding the rows that stay.
/// </summary>
public sealed partial class SearchHitViewModel : ObservableObject
{
    private readonly Action<SearchHitViewModel> _open;

    public SearchHitViewModel(SearchHit hit, Action<SearchHitViewModel> open)
    {
        ArgumentNullException.ThrowIfNull(hit);
        ArgumentNullException.ThrowIfNull(open);
        _open = open;
        Id = hit.Row.Id;
        Update(hit);
    }

    /// <summary>The task id (the reconcile key).</summary>
    public long Id { get; }

    [ObservableProperty] public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty] public partial bool Done { get; private set; }

    [ObservableProperty] public partial Priority Priority { get; private set; }

    /// <summary>"#work", or empty.</summary>
    [ObservableProperty] public partial string TagLabel { get; private set; } = string.Empty;

    /// <summary>Where the task lives: "Inbox", "Mon 16 Nov", "20–22 Nov".</summary>
    [ObservableProperty] public partial string Context { get; private set; } = string.Empty;

    /// <summary>The keyboard selection (Overlay.Selected background).</summary>
    [ObservableProperty] public partial bool IsSelected { get; set; }

    public bool HasTag => TagLabel.Length > 0;

    /// <summary>Accessible name: "Weekly review, Sun 22 Nov".</summary>
    public string AccessibleName => string.Concat(Title, ", ", Context);

    /// <summary>The record last pushed in.</summary>
    public SearchHit Hit { get; private set; } = null!;

    public void Update(SearchHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (ReferenceEquals(Hit, hit) || Equals(Hit, hit))
        {
            return;
        }

        Hit = hit;
        Title = hit.Row.Title;
        Done = hit.Row.Done;
        Priority = hit.Row.Priority;
        TagLabel = hit.Row.TagLabel;
        Context = hit.Context;
        OnPropertyChanged(nameof(HasTag));
        OnPropertyChanged(nameof(AccessibleName));
    }

    /// <summary>Click: open this task's edit dialog.</summary>
    [RelayCommand] private void Open() => _open(this);
}
