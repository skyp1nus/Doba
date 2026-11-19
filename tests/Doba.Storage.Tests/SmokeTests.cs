namespace Doba.Storage.Tests;

public class SmokeTests
{
    [Fact]
    public void Storage_assembly_loads() => Assert.Equal("Doba.Storage", typeof(Doba.Storage.AssemblyMarker).Assembly.GetName().Name);
}
