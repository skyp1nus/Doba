using Doba.Storage;

namespace Doba.Bridge.Tests;

public sealed class BridgeNamesTests
{
    // Expected keys computed with the code SingleInstance.KeyFor had before it moved here (full path, no trailing
    // separator, upper-cased invariant, SHA-256, first 16 bytes as hex): a running Doba 0.1 and a new Doba.Mcp must
    // agree on the mutex name.
    [Theory]
    [InlineData(@"C:\Users\Someone\AppData\Local\Doba", "269D1D34BCFB1C8AEB3C6DC4854A4D26")]
    [InlineData(@"D:\dev\Doba\artifacts\manual\", "4684AE95F0C6AC94D2574C603C4C4DAA")]
    [InlineData(@"d:\DEV\doba\artifacts\manual", "4684AE95F0C6AC94D2574C603C4C4DAA")]
    [InlineData("C:\\Users\\\u0412\u043b\u0430\u0434\\Doba", "374CE2B1A7FE6CAABA1CBE2B2EFBCD16")]
    public void Instance_key_is_the_one_single_instance_used(string folder, string expected)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows paths");
        Assert.Equal(expected, BridgeNames.InstanceKey(folder));
    }

    [Fact]
    public void Mutex_and_pipe_names_carry_the_key()
    {
        var folder = Path.Combine(Path.GetTempPath(), "doba-bridge-names");
        var key = BridgeNames.InstanceKey(folder);
        Assert.Equal(32, key.Length);
        Assert.Equal(@"Local\Doba-" + key, BridgeNames.MutexName(folder));
        Assert.Equal("Doba-" + key + "-mcp", BridgeNames.PipeName(folder));
    }

    [Fact]
    public void Default_data_folder_is_storage_default() =>
        Assert.Equal(DataLocation.Default().Folder, BridgeNames.DefaultDataFolder());

    [Fact]
    public void Empty_folder_is_rejected() =>
        Assert.ThrowsAny<ArgumentException>(() => BridgeNames.InstanceKey(" "));
}
