using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Doba.Core.Projections;

namespace Doba.App.Shell;

/// <summary>
/// One tag row in the app sidebar (keyed by <see cref="Name"/>). Long-lived: <see cref="Update"/> pushes the fresh
/// <see cref="TagEntry"/> so a count or filter change updates this row in place.
/// </summary>
public sealed partial class TagItemViewModel : ObservableObject
{
    private readonly Action<TagItemViewModel> _toggle;

    public TagItemViewModel(TagEntry entry, Action<TagItemViewModel> toggle)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(toggle);
        _toggle = toggle;
        Name = entry.Name;
        // The first user-perceived character (a surrogate pair or a combining sequence stays whole), upper-cased.
        Initial = Name.Length > 0 ? Name[..StringInfo.GetNextTextElementLength(Name)].ToUpperInvariant() : string.Empty;
        Update(entry);
    }

    /// <summary>The normalised tag name (the key; also the label).</summary>
    public string Name { get; }

    /// <summary>Upper-case first character (text element), shown in the rail badge.</summary>
    public string Initial { get; }

    /// <summary>Open tasks with this tag (never dimmed by the filter).</summary>
    [ObservableProperty] public partial int Count { get; private set; }

    [ObservableProperty] public partial string CountText { get; private set; } = string.Empty;

    /// <summary>This tag is the sidebar filter (pressed state).</summary>
    [ObservableProperty] public partial bool IsActive { get; private set; }

    /// <summary>The record last pushed in.</summary>
    public TagEntry Model { get; private set; } = null!;

    public void Update(TagEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Model = entry;
        Count = entry.OpenCount;
        CountText = entry.OpenCount.ToString(CultureInfo.InvariantCulture);
        IsActive = entry.Active;
    }

    /// <summary>Click: set or clear the tag filter.</summary>
    [RelayCommand]
    private void Toggle() => _toggle(this);
}
