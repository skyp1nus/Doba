using System.Collections.ObjectModel;
using Doba.App.Services;

namespace Doba.App.Tests.Services;

public class KeyedSyncTests
{
    private sealed class Vm(int key, string text)
    {
        public int Key { get; } = key;
        public string Text { get; set; } = text;
    }

    private static int Sync(ObservableCollection<Vm> target, params (int Key, string Text)[] source) =>
        KeyedSync.Apply(target, source, r => r.Key, v => v.Key, r => new Vm(r.Key, r.Text), (v, r) => v.Text = r.Text);

    [Fact]
    public void Updates_in_place_and_keeps_instances()
    {
        var target = new ObservableCollection<Vm>();
        Sync(target, (1, "a"), (2, "b"), (3, "c"));
        var second = target[1];
        var resets = 0;
        target.CollectionChanged += (_, e) => { if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++; };

        var created = Sync(target, (1, "a"), (2, "B"), (3, "c"));

        Assert.Equal(0, created);
        Assert.Same(second, target[1]);
        Assert.Equal("B", second.Text);
        Assert.Equal(0, resets);
    }

    [Fact]
    public void Inserts_removes_and_reorders()
    {
        var target = new ObservableCollection<Vm>();
        Sync(target, (1, "a"), (2, "b"), (3, "c"));
        var three = target[2];

        var created = Sync(target, (3, "c"), (4, "d"), (1, "a"));

        Assert.Equal(1, created);
        Assert.Equal([3, 4, 1], target.Select(v => v.Key));
        Assert.Same(three, target[0]);
    }

    private static int SyncStable(ObservableCollection<Vm> target, params (int Key, string Text)[] source) =>
        KeyedSync.ApplyStable(target, source, r => r.Key, v => v.Key, r => new Vm(r.Key, r.Text), (v, r) => v.Text = r.Text);

    [Fact]
    public void Stable_mode_never_moves_items_when_the_source_order_changes()
    {
        var target = new ObservableCollection<Vm>();
        SyncStable(target, (1, "a"), (2, "b"), (3, "c"));
        var items = target.ToList();
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, e) => actions.Add(e.Action);

        var created = SyncStable(target, (3, "C"), (1, "A"), (2, "B"));

        Assert.Equal(0, created);
        Assert.Empty(actions); // no Move, no Remove + Add: the items' containers stay
        Assert.Equal(items, target);
        Assert.Equal(["A", "B", "C"], target.Select(v => v.Text));
    }

    [Fact]
    public void Stable_mode_removes_stale_items_and_appends_new_ones_in_source_order()
    {
        var target = new ObservableCollection<Vm>();
        SyncStable(target, (1, "a"), (2, "b"), (3, "c"));
        var one = target[0];
        var three = target[2];
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, e) => actions.Add(e.Action);

        var created = SyncStable(target, (5, "e"), (3, "c"), (4, "d"), (1, "a"));

        Assert.Equal(2, created);
        Assert.Equal([1, 3, 5, 4], target.Select(v => v.Key)); // kept in place, new ones appended in source order
        Assert.Same(one, target[0]);
        Assert.Same(three, target[1]);
        Assert.Equal(
            [System.Collections.Specialized.NotifyCollectionChangedAction.Remove,
             System.Collections.Specialized.NotifyCollectionChangedAction.Add,
             System.Collections.Specialized.NotifyCollectionChangedAction.Add],
            actions);

        Assert.Equal(0, SyncStable(target));
        Assert.Empty(target);
    }
}
