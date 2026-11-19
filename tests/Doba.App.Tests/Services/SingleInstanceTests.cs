using Doba.App.Services;

namespace Doba.App.Tests.Services;

/// <summary>
/// One Doba process per data folder (<see cref="SingleInstance"/>). The mutex is thread-affine, so each "process" here
/// is its own thread that claims and releases; folders are fresh paths under the temp directory (never the real one).
/// </summary>
public sealed class SingleInstanceTests
{
    private static string NewFolder() =>
        Path.Combine(Path.GetTempPath(), "doba-tests", "instance-" + Guid.NewGuid().ToString("N"));

    /// <summary>Runs <paramref name="action"/> on a thread of its own and waits for it (a second process's Main).</summary>
    private static T OnOtherThread<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
#pragma warning disable CA1031 // rethrown on the test thread below
            catch (Exception ex)
#pragma warning restore CA1031
            {
                error = ex;
            }
        });
        thread.Start();
        thread.Join();
        return error is null ? result : throw new InvalidOperationException("The other thread failed.", error);
    }

    [Fact]
    public void A_second_claim_on_the_same_folder_is_refused_until_the_first_is_released()
    {
        var folder = NewFolder();
        using var first = SingleInstance.Claim(folder);
        Assert.True(first.IsPrimary);

        var second = OnOtherThread(() =>
        {
            using var claim = SingleInstance.Claim(folder);
            return claim.IsPrimary;
        });
        Assert.False(second);

        first.Dispose();
        var afterRelease = OnOtherThread(() =>
        {
            using var claim = SingleInstance.Claim(folder);
            return claim.IsPrimary;
        });
        Assert.True(afterRelease);
    }

    [Fact]
    public void Other_folders_are_not_blocked()
    {
        using var first = SingleInstance.Claim(NewFolder());
        var other = NewFolder();

        Assert.True(OnOtherThread(() =>
        {
            using var claim = SingleInstance.Claim(other);
            return claim.IsPrimary;
        }));
    }

    [Fact]
    public void The_same_folder_spelled_differently_has_one_key()
    {
        var folder = NewFolder();

        Assert.Equal(SingleInstance.KeyFor(folder), SingleInstance.KeyFor(folder + Path.DirectorySeparatorChar));
        Assert.Equal(SingleInstance.KeyFor(folder), SingleInstance.KeyFor(folder.ToUpperInvariant()));
        Assert.Equal(SingleInstance.KeyFor(folder), SingleInstance.KeyFor(Path.Combine(folder, "sub", "..")));
        Assert.NotEqual(SingleInstance.KeyFor(folder), SingleInstance.KeyFor(NewFolder()));
    }

    [Fact]
    public void A_claim_left_by_a_dead_process_is_taken_over()
    {
        var folder = NewFolder();
        // The thread ends without releasing: the mutex is abandoned, as when a process crashes.
        OnOtherThread(() =>
        {
            var claim = SingleInstance.Claim(folder);
            return claim.IsPrimary;
        });

        Assert.True(OnOtherThread(() =>
        {
            using var claim = SingleInstance.Claim(folder);
            return claim.IsPrimary;
        }));
    }

    [Fact]
    public void A_second_launch_asks_the_first_to_show_its_window()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // named events exist only on Windows; elsewhere a second launch just exits
        }

        var folder = NewFolder();
        using var first = SingleInstance.Claim(folder);
        using var shown = new SemaphoreSlim(0);
        first.ListenForShowRequests(() => shown.Release());

        OnOtherThread(() =>
        {
            using var second = SingleInstance.Claim(folder);
            second.SignalPrimary();
            return second.IsPrimary;
        });

        Assert.True(shown.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(0, shown.CurrentCount);
    }

    [Fact]
    public void A_request_made_before_the_first_listens_is_not_lost()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var folder = NewFolder();
        using var first = SingleInstance.Claim(folder);
        OnOtherThread(() =>
        {
            using var second = SingleInstance.Claim(folder);
            second.SignalPrimary();
            return 0;
        });

        using var shown = new SemaphoreSlim(0);
        first.ListenForShowRequests(() => shown.Release());
        Assert.True(shown.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }
}
