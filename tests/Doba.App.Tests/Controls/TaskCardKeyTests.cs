using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Doba.App.Controls;
using Doba.App.Tests.Support;
using Doba.App.ViewModels;
using Doba.Core.Projections;

namespace Doba.App.Tests.Controls;

public class TaskCardKeyTests
{
    [AvaloniaFact]
    public void Held_Space_toggles_the_card_once_per_press()
    {
        using var harness = new ScreenHarness();
        var store = harness.App.Store;
        var task = store.OnDate(harness.App.DayClock.Today).First(t => !t.Done);
        var ctx = new ProjectionContext(harness.App.DayClock.Today, harness.App.DayClock.Now, harness.App.Settings.Current.WeekStart, null);
        var row = new TaskRowViewModel(Projector.Search(store, ctx, task.Title).First(h => h.Row.Id == task.Id).Row,
            harness.Actions, harness.Commands);
        store.Changed += (_, _) => row.Update(row.Row with { Done = store.Get(task.Id)!.Done });
        var card = new TaskCard { DataContext = row };
        var window = new Window { Width = 300, Height = 80, Content = card };
        window.Show();
        window.UpdateLayout();

        // The window itself does not swallow repeats here, so this checks the card's own guard.
        var title = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_TitleButton");
        Assert.True(title.Focus(NavigationMethod.Tab));
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
        Assert.True(store.Get(task.Id)!.Done);

        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); // auto-repeat of the held key
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
        Assert.True(store.Get(task.Id)!.Done);
        Assert.Empty(harness.Commands.Edits); // the title button did not click either

        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); // a new press
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
        Assert.False(store.Get(task.Id)!.Done);
        Assert.Empty(harness.Commands.Edits);
        window.Close();
    }
}
