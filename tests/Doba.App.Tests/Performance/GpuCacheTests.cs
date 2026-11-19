using Avalonia;

namespace Doba.App.Tests.Performance;

/// <summary>The Skia GPU resource cache cap set in Program.BuildAvaloniaApp (measured with scripts/perf.ps1).</summary>
public sealed class GpuCacheTests
{
    [Fact]
    public void The_gpu_cache_cap_sits_between_the_measured_regression_and_avalonias_default()
    {
        // Avalonia's own default (12 textures of 1024×600×4); the cap is what saves idle memory.
        var avaloniaDefault = new SkiaOptions().MaxGpuResourceSizeBytes;
        Assert.Equal(29_491_200L, avaloniaDefault);
        Assert.True(Program.GpuResourceCacheBytes < avaloniaDefault);

        // At 20 MB and below the 10k and dense weeks re-upload textures during transitions (render p95 5.6 → 11.1 ms).
        Assert.True(Program.GpuResourceCacheBytes > 20L * 1024 * 1024);
    }
}
