using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Doba.App.Dialogs;
using Doba.App.Shell;
using Doba.App.Tests.Support;
using Doba.Core.Navigation;
using static Doba.App.Tests.Keyboard.KeyboardKit;

namespace Doba.App.Tests.Keyboard;

/// <summary>The G I / G T chord: armed by G, completed by I / T within <see cref="KeyboardShortcuts.ChordTimeout"/>,
/// disarmed by any other key or the timeout. Time is a <see cref="ManualClock"/>: no real waiting.</summary>
public class ChordTests
{
    private static (ShellFixture Shell, ManualClock Clock, KeyboardShortcuts Keys) Open()
    {
        var shell = ShellFixture.Open(AppView.Week);
        var clock = new ManualClock();
        var keys = shell.Window.Shortcuts!;
        keys.ChordClock = clock;
        return (shell, clock, keys);
    }

    [AvaloniaFact]
    public void G_arms_the_chord_without_doing_anything_else()
    {
        var (shell, _, keys) = Open();
        using var _ = shell;
        var start = shell.Vm.Nav;
        Assert.False(keys.IsChordArmed);
        shell.Key(Key.G);
        Assert.True(keys.IsChordArmed);
        Assert.Equal(start, shell.Vm.Nav);
        Assert.Null(shell.Vm.Overlay);
    }

    [AvaloniaFact]
    public void I_or_T_completes_it_and_disarms_it()
    {
        var (shell, _, keys) = Open();
        using var _ = shell;
        shell.Key(Key.G);
        shell.Key(Key.I);
        Assert.Equal(AppView.Inbox, shell.Vm.Nav.View);
        Assert.False(keys.IsChordArmed);

        shell.Key(Key.T);                                                         // T alone is nothing
        Assert.Equal(AppView.Inbox, shell.Vm.Nav.View);
        shell.Key(Key.G);
        shell.Key(Key.T);
        Assert.Equal(AppView.Today, shell.Vm.Nav.View);
    }

    [AvaloniaFact]
    public void Another_key_disarms_it_and_then_acts_as_itself()
    {
        var (shell, _, keys) = Open();
        using var _ = shell;
        var start = shell.Vm.Nav;

        shell.Key(Key.G);
        shell.Key(Key.N);                                                         // N: New task, as usual
        Assert.False(keys.IsChordArmed);
        Assert.IsType<TaskDialogViewModel>(shell.Vm.Overlay);
        shell.Key(Key.Escape);
        shell.Key(Key.I);
        Assert.Equal(start, shell.Vm.Nav);

        shell.Key(Key.G);
        shell.Key(Key.Tab);                                                       // a key a control handles (focus moves)
        Assert.False(keys.IsChordArmed);
        shell.Key(Key.I);
        Assert.Equal(start, shell.Vm.Nav);

        shell.Key(Key.G);
        shell.Key(Key.D1);                                                        // a digit: disarms, then zooms
        Assert.Equal(AppView.Year, shell.Vm.Nav.View);
        shell.Key(Key.T);
        Assert.Equal(AppView.Year, shell.Vm.Nav.View);
    }

    [AvaloniaFact]
    public void A_lone_modifier_press_or_a_held_G_keeps_it_armed()
    {
        var (shell, _, keys) = Open();
        using var _ = shell;
        shell.Key(Key.G);
        shell.Key(Key.LeftShift);
        shell.Key(Key.LeftCtrl);
        Assert.True(keys.IsChordArmed);
        shell.Key(Key.I);
        Assert.Equal(AppView.Inbox, shell.Vm.Nav.View);

        Down(shell, Key.G);
        Down(shell, Key.G);                                                       // auto-repeat
        Release(shell, Key.G);
        shell.Key(Key.T);
        Assert.Equal(AppView.Today, shell.Vm.Nav.View);
    }

    [AvaloniaFact]
    public void It_expires_after_the_timeout()
    {
        var (shell, clock, keys) = Open();
        using var _ = shell;
        var start = shell.Vm.Nav;

        shell.Key(Key.G);
        clock.Advance(KeyboardShortcuts.ChordTimeout - TimeSpan.FromMilliseconds(1));
        Assert.True(keys.IsChordArmed);
        shell.Key(Key.I);
        Assert.Equal(AppView.Inbox, shell.Vm.Nav.View);

        shell.Vm.SetZoomLevel(AppView.Week);
        shell.Settle();
        start = shell.Vm.Nav;
        shell.Key(Key.G);
        clock.Advance(KeyboardShortcuts.ChordTimeout + TimeSpan.FromMilliseconds(1));
        Assert.False(keys.IsChordArmed);
        shell.Key(Key.I);
        Assert.Equal(start, shell.Vm.Nav);
    }

    [AvaloniaFact]
    public void The_timeout_is_a_named_constant_not_a_motion_token()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(KeyboardShortcuts.ChordTimeoutMilliseconds), KeyboardShortcuts.ChordTimeout);
        Assert.InRange(KeyboardShortcuts.ChordTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }
}
