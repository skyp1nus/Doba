using System.Collections.ObjectModel;

namespace Doba.App.Services;

/// <summary>
/// Reconciles a bound <see cref="ObservableCollection{T}"/> of view-models with a freshly projected list of
/// immutable records, by key. Existing items are updated in place (so their visuals stay alive and animate),
/// missing ones are inserted at the right index, stale ones removed, moved ones moved. The collection is never cleared.
/// This is what keeps "complete a task" a local change instead of a screen rebuild (ARCHITECTURE §2).
/// </summary>
public static class KeyedSync
{
    /// <param name="target">The bound collection.</param>
    /// <param name="source">The new projection, in display order. Keys must be unique.</param>
    /// <param name="keyOfSource">Key of a record.</param>
    /// <param name="keyOfTarget">Key of an existing view-model.</param>
    /// <param name="create">Creates a view-model for a new record.</param>
    /// <param name="update">Pushes a record into an existing view-model (should be cheap when nothing changed).</param>
    /// <returns>The number of items created (useful to trigger the new-row entrance animation).</returns>
    public static int Apply<TRecord, TViewModel, TKey>(
        ObservableCollection<TViewModel> target,
        IReadOnlyList<TRecord> source,
        Func<TRecord, TKey> keyOfSource,
        Func<TViewModel, TKey> keyOfTarget,
        Func<TRecord, TViewModel> create,
        Action<TViewModel, TRecord> update)
        where TKey : notnull
        where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        // 1. Remove items whose key is gone.
        var wanted = new HashSet<TKey>(source.Count);
        foreach (var r in source)
        {
            wanted.Add(keyOfSource(r));
        }

        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(keyOfTarget(target[i])))
            {
                target.RemoveAt(i);
            }
        }

        // 2. Walk the source in order; reuse, move or create.
        var existing = new Dictionary<TKey, TViewModel>(target.Count);
        foreach (var vm in target)
        {
            existing[keyOfTarget(vm)] = vm;
        }

        var created = 0;
        for (var i = 0; i < source.Count; i++)
        {
            var record = source[i];
            var key = keyOfSource(record);
            if (existing.TryGetValue(key, out var vm))
            {
                // Items before i are already placed, so search from i; in the common (unchanged order) case this is O(1).
                var at = i;
                while (at < target.Count && !ReferenceEquals(target[at], vm))
                {
                    at++;
                }

                if (at != i)
                {
                    target.Move(at, i);
                }

                update(vm, record);
            }
            else
            {
                target.Insert(i, create(record));
                created++;
            }
        }

        return created;
    }
}
