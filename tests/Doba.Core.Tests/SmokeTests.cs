namespace Doba.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Core_assembly_loads() => Assert.Equal("Doba.Core", typeof(Doba.Core.AssemblyMarker).Assembly.GetName().Name);
}
