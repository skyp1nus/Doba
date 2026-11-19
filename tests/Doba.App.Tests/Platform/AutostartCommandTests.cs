using Doba.App.Platform;

namespace Doba.App.Tests.Platform;

public class AutostartCommandTests
{
    private const string ExeWithSpaces = @"C:\Users\Jane Doe\AppData\Local\Programs\Doba\Doba.exe";

    [Fact]
    public void Build_quotes_paths_with_spaces_and_appends_the_minimized_flag()
    {
        var command = AutostartCommand.Build(ExeWithSpaces);

        Assert.Equal("\"" + ExeWithSpaces + "\" --minimized", command);
        Assert.Equal(AutostartCommand.MinimizedFlag, command[(command.LastIndexOf('"') + 2)..]);
    }

    [Fact]
    public void Build_quotes_paths_without_spaces_too()
    {
        Assert.Equal("\"C:\\Doba\\Doba.exe\" --minimized", AutostartCommand.Build(@"C:\Doba\Doba.exe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("C:\\a\"b\\Doba.exe")]
    public void Build_rejects_empty_paths_and_paths_with_quotes(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => AutostartCommand.Build(path));
    }

    [Fact]
    public void Extracts_the_executable_from_the_command_it_built()
    {
        Assert.Equal(ExeWithSpaces, AutostartCommand.ExtractExePath(AutostartCommand.Build(ExeWithSpaces)));
    }

    [Theory]
    [InlineData(@"C:\Doba\Doba.exe --minimized", @"C:\Doba\Doba.exe")]
    [InlineData(@"C:\Doba\Doba.exe", @"C:\Doba\Doba.exe")]
    [InlineData(@"  ""C:\Program Files\Doba\Doba.exe""  ", @"C:\Program Files\Doba\Doba.exe")]
    public void Extracts_the_executable_from_hand_written_entries(string commandLine, string expected)
    {
        Assert.Equal(expected, AutostartCommand.ExtractExePath(commandLine));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"")]
    [InlineData("\"\" --minimized")]
    public void Extract_returns_null_for_empty_or_malformed_entries(string? commandLine)
    {
        Assert.Null(AutostartCommand.ExtractExePath(commandLine));
    }

    [Fact]
    public void Matches_only_the_same_executable_ignoring_case()
    {
        var stored = AutostartCommand.Build(ExeWithSpaces);

        Assert.True(AutostartCommand.Matches(stored, ExeWithSpaces));
        Assert.True(AutostartCommand.Matches(stored, ExeWithSpaces.ToUpperInvariant()));
        Assert.False(AutostartCommand.Matches(stored, @"D:\Portable\Doba\Doba.exe"));
        Assert.False(AutostartCommand.Matches(stored, null));
        Assert.False(AutostartCommand.Matches(null, ExeWithSpaces));
    }
}
