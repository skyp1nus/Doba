using CommunityToolkit.Mvvm.ComponentModel;

namespace Doba.App.Shell;

/// <summary>
/// Base for in-window overlays (task dialog, search palette) shown by the shell's OverlayHost through
/// <c>MainViewModel.OpenOverlay</c>. Overlays are panels inside the main window, never Avalonia Popup windows.
/// At most one overlay is open; the host owns open/close animations and keyboard routing.
/// </summary>
public abstract partial class OverlayViewModel : ObservableObject
{
    /// <summary>Raised when the overlay wants to close (Esc not handled internally, Save, a picked search hit). The
    /// host plays the exit animation, then calls <c>MainViewModel.CloseOverlay</c>, which calls <see cref="OnClosed"/>.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Asks the host to close this overlay. Do not commit work here; commit in <see cref="OnClosed"/>.</summary>
    protected void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Esc pressed while this overlay is open. Return true when handled internally (e.g. a popover was
    /// closed); false lets the host close the overlay.</summary>
    public virtual bool OnEscape() => false;

    /// <summary>Called once by the host AFTER the exit animation has finished and the overlay is removed from the
    /// tree. The task dialog commits its save here, so the store change does not re-render under the closing dialog.</summary>
    public virtual void OnClosed()
    {
    }
}

/// <summary>Implemented by every overlay view; the host calls it once the view is in the tree and visible.</summary>
public interface IOverlayView
{
    /// <summary>Moves keyboard focus to the overlay's first input (task name box, search box).</summary>
    void FocusInitial();
}
