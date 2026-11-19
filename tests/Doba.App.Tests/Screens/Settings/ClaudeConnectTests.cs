using System.Text.Json;
using Doba.App.Screens.Settings;
using Doba.Bridge;

namespace Doba.App.Tests.Screens.Settings;

/// <summary>The texts Data › Connect Claude copies (pure functions; nothing is copied or launched here).</summary>
public class ClaudeConnectTests
{
    private const string Exe = @"C:\Program Files\Doba\Doba.Mcp.exe";
    private const string Default = @"C:\Users\someone\AppData\Local\Doba";
    private const string Custom = @"D:\dev\Doba\artifacts\manual";

    [Fact]
    public void Desktop_config_for_the_default_folder_has_no_args()
    {
        var text = ClaudeConnect.DesktopConfig(Exe, Default, Default);

        Assert.Equal(
            "{\n" +
            "  \"mcpServers\": {\n" +
            "    \"doba\": {\n" +
            "      \"command\": \"C:\\\\Program Files\\\\Doba\\\\Doba.Mcp.exe\"\n" +
            "    }\n" +
            "  }\n" +
            "}",
            text);
    }

    [Fact]
    public void Desktop_config_for_another_folder_passes_it_with_data()
    {
        var text = ClaudeConnect.DesktopConfig(Exe, Custom, Default);

        Assert.Contains("\"args\": [\n        \"--data\",\n        \"D:\\\\dev\\\\Doba\\\\artifacts\\\\manual\"\n      ]", text, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(text);
        var doba = json.RootElement.GetProperty("mcpServers").GetProperty("doba");
        Assert.Equal(Exe, doba.GetProperty("command").GetString());
        Assert.Equal(["--data", Custom], doba.GetProperty("args").EnumerateArray().Select(a => a.GetString()));
    }

    [Fact]
    public void Desktop_config_escapes_backslashes_and_quotes_and_keeps_other_characters_readable()
    {
        const string odd = "C:\\Users\\Олена \"O'Neil\"\\Doba.Mcp.exe";
        const string folder = "E:\\Дані\\a&b <c>";

        var text = ClaudeConnect.DesktopConfig(odd, folder, Default);

        Assert.Contains("\"C:\\\\Users\\\\Олена \\\"O'Neil\\\"\\\\Doba.Mcp.exe\"", text, StringComparison.Ordinal);
        Assert.Contains("\"E:\\\\Дані\\\\a&b <c>\"", text, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(text);
        var doba = json.RootElement.GetProperty("mcpServers").GetProperty("doba");
        Assert.Equal(odd, doba.GetProperty("command").GetString());
        Assert.Equal(folder, doba.GetProperty("args")[1].GetString());
    }

    [Fact]
    public void Code_command_for_the_default_folder()
    {
        Assert.Equal(@"claude mcp add --scope user doba -- 'C:\Program Files\Doba\Doba.Mcp.exe'",
            ClaudeConnect.CodeCommand(Exe, Default, Default));
    }

    [Fact]
    public void Code_command_for_another_folder()
    {
        Assert.Equal(@"claude mcp add --scope user doba -- 'C:\Program Files\Doba\Doba.Mcp.exe' --data 'D:\dev\Doba\artifacts\manual'",
            ClaudeConnect.CodeCommand(Exe, Custom, Default));
    }

    /// <summary>The command is for PowerShell: single quotes keep $, backticks, parentheses and % literal (inside double
    /// quotes "$2026" would expand to nothing); a quote inside is doubled, typographic ones too.</summary>
    [Theory]
    [InlineData(@"C:\Users\a$2026\Doba.Mcp.exe", @"'C:\Users\a$2026\Doba.Mcp.exe'")]
    [InlineData(@"C:\Users\a`b\Doba.Mcp.exe", @"'C:\Users\a`b\Doba.Mcp.exe'")]
    [InlineData(@"C:\Users\O'Brien\Doba.Mcp.exe", @"'C:\Users\O''Brien\Doba.Mcp.exe'")]
    [InlineData(@"C:\Users\O’Brien\Doba.Mcp.exe", @"'C:\Users\O’’Brien\Doba.Mcp.exe'")]
    [InlineData(@"C:\Program Files (x86)\%TEMP%\Doba.Mcp.exe", @"'C:\Program Files (x86)\%TEMP%\Doba.Mcp.exe'")]
    public void Code_command_quotes_the_exe_for_powershell(string exe, string quoted) =>
        Assert.Equal("claude mcp add --scope user doba -- " + quoted, ClaudeConnect.CodeCommand(exe, Default, Default));

    [Theory]
    [InlineData(@"D:\data $x", @" --data 'D:\data $x'")]
    [InlineData(@"D:\it's `here` 100%", @" --data 'D:\it''s `here` 100%'")]
    [InlineData(@"D:\", @" --data 'D:\.'")]
    [InlineData(@"E:\\", @" --data 'E:\.'")]
    public void Code_command_quotes_the_data_folder_and_no_argument_ends_in_a_backslash(string folder, string tail)
    {
        var command = ClaudeConnect.CodeCommand(Exe, folder, Default);
        Assert.EndsWith(tail, command, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\'", command, StringComparison.Ordinal);
    }

    [Fact]
    public void A_root_folder_written_with_a_dot_is_the_same_folder_to_doba_mcp() =>
        Assert.Equal(BridgeNames.PipeName(@"D:\"), BridgeNames.PipeName(@"D:\."));

    [Theory]
    [InlineData(@"C:\Users\someone\AppData\Local\Doba", true)]
    [InlineData(@"c:\users\SOMEONE\appdata\local\doba", true)]
    [InlineData(@"C:\Users\someone\AppData\Local\Doba\", true)]
    [InlineData(@"C:\Users\someone\AppData\Local\Doba\..\Doba", true)]
    [InlineData(@"C:\Users\someone\AppData\Local\Doba2", false)]
    [InlineData(@"D:\Doba", false)]
    public void The_default_folder_is_compared_as_a_full_path_ignoring_case(string folder, bool isDefault)
    {
        Assert.Equal(isDefault, ClaudeConnect.IsDefaultFolder(folder, Default));
        Assert.Equal(isDefault, !ClaudeConnect.CodeCommand(Exe, folder, Default).Contains("--data", StringComparison.Ordinal));
        Assert.Equal(isDefault, !ClaudeConnect.DesktopConfig(Exe, folder, Default).Contains("--data", StringComparison.Ordinal));
    }

    [Fact]
    public void A_custom_folder_is_passed_as_a_full_path_without_a_trailing_separator()
    {
        Assert.EndsWith(@" --data 'D:\dev\Doba\artifacts\manual'", ClaudeConnect.CodeCommand(Exe, Custom + @"\", Default), StringComparison.Ordinal);
    }

    [Fact]
    public void The_server_exe_is_next_to_the_app()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Doba.Mcp.exe"), ClaudeConnect.McpExePath);
        Assert.True(ClaudeConnect.IsDefaultFolder(BridgeNames.DefaultDataFolder(), BridgeNames.DefaultDataFolder()));
    }
}
