using CommunityToolkit.Mvvm.ComponentModel;
using Doba.App.Screens;
using Doba.App.Shell;
using Doba.Core.Editing;

namespace Doba.App.Dialogs;

/// <summary>
/// The New / Edit task dialog. Every rule (pill labels, date/time stepping, wheel commit, save) is in
/// <see cref="TaskDialog"/> (Core); this view-model holds the current <see cref="TaskDraft"/> and replaces it on each
/// edit. The store is written in <see cref="OverlayViewModel.OnClosed"/>, after the exit animation. Placeholder until
/// the Task dialog package.
/// </summary>
public sealed partial class TaskDialogViewModel : OverlayViewModel
{
    public TaskDialogViewModel(ScreenServices services, TaskDraft draft)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(draft);
        Services = services;
        Draft = draft;
    }

    /// <summary>Store, settings, clock and task actions.</summary>
    public ScreenServices Services { get; }

    /// <summary>The draft being edited (immutable record; replaced, never mutated).</summary>
    [ObservableProperty] public partial TaskDraft Draft { get; set; }
}
