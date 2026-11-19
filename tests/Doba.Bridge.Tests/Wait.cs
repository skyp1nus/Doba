using System.Diagnostics;

namespace Doba.Bridge.Tests;

internal static class Wait
{
    /// <summary>Generous for a loaded machine; a passing wait returns as soon as the condition holds.</summary>
    public static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan s_poll = TimeSpan.FromMilliseconds(20);

    /// <summary>Polls <paramref name="condition"/> until it holds; fails the test after <paramref name="timeout"/>.</summary>
    public static async Task UntilAsync(Func<bool> condition, string what, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > (timeout ?? Long))
            {
                Assert.Fail("Timed out waiting for: " + what);
            }

            await Task.Delay(s_poll);
        }
    }
}
