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
}
