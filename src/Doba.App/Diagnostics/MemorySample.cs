using System.Diagnostics;
using System.Globalization;

namespace Doba.App.Diagnostics;

/// <summary>The process's memory at one moment of a perf scenario, in bytes.</summary>
/// <param name="WorkingSet">Physical memory in use (what Task Manager's "Memory" column is close to).</param>
/// <param name="PrivateBytes">Committed private memory (Task Manager's "Commit size").</param>
/// <param name="ManagedHeap">Managed objects on the heap right now, garbage included (no collection is forced).</param>
/// <param name="GcCommitted">Memory the GC had committed at its last collection (0 before the first).</param>
internal readonly record struct MemorySample(long WorkingSet, long PrivateBytes, long ManagedHeap, long GcCommitted)
{
    /// <summary>Reads the counters now. Nothing is collected or trimmed for it.</summary>
    public static MemorySample Take()
    {
        using var process = Process.GetCurrentProcess();
        return new MemorySample(process.WorkingSet64, process.PrivateMemorySize64,
            GC.GetTotalMemory(forceFullCollection: false), GC.GetGCMemoryInfo().TotalCommittedBytes);
    }

    /// <summary><c>prefix-working-set-mb=… prefix-private-mb=… prefix-managed-heap-mb=… prefix-gc-committed-mb=…</c></summary>
    public string Format(string prefix) => string.Create(CultureInfo.InvariantCulture,
        $"{prefix}-working-set-mb={Mb(WorkingSet)} {prefix}-private-mb={Mb(PrivateBytes)} " +
        $"{prefix}-managed-heap-mb={Mb(ManagedHeap)} {prefix}-gc-committed-mb={Mb(GcCommitted)}");

    private static string Mb(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture);
}
