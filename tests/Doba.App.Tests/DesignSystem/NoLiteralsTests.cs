using System.Text.RegularExpressions;

namespace Doba.App.Tests.DesignSystem;

/// <summary>
/// Every colour comes from Theme/Tokens.axaml and every duration from Motion: no colour or duration literals in
/// src/Doba.App outside Theme/ and Motion/.
/// </summary>
public partial class NoLiteralsTests
{
    [Fact]
    public void App_sources_outside_Theme_and_Motion_have_no_colour_or_duration_literals()
    {
        var root = Path.Combine(RepoRoot(), "src", "Doba.App");
        var violations = new List<string>();
        foreach (var file in SourceFiles(root))
        {
            var lines = File.ReadAllLines(file);
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            for (var i = 0; i < lines.Length; i++)
            {
                if (IsAllowed(relative, lines[i]))
                {
                    continue;
                }

                foreach (var finding in Scan(lines[i], Path.GetExtension(file)))
                {
                    violations.Add($"{Path.GetRelativePath(RepoRoot(), file)}:{i + 1}: {finding}  →  {lines[i].Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Use Theme/Tokens.axaml resources and Motion tokens instead of literals:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void Scanner_finds_the_files_it_must_check()
    {
        var root = Path.Combine(RepoRoot(), "src", "Doba.App");
        var files = SourceFiles(root).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).ToList();
        Assert.Contains("MainWindow.axaml", files);
        Assert.Contains("App.axaml", files);
        Assert.Contains("Controls/PriorityIcon.cs", files);
        Assert.DoesNotContain(files, f => f.StartsWith("Theme/", StringComparison.Ordinal) || f.StartsWith("Motion/", StringComparison.Ordinal) || f.StartsWith("Platform/", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.StartsWith("obj/", StringComparison.Ordinal) || f.StartsWith("bin/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("<Border Background=\"#17171A\" />", ".axaml")]
    [InlineData("<Border Background=\"#FFF\" />", ".axaml")]
    [InlineData("<SolidColorBrush Color=\"#1A9FB4C7\" />", ".axaml")]
    [InlineData("<TextBlock Foreground=\"Red\" />", ".axaml")]
    [InlineData("<DoubleTransition Duration=\"0:0:0.2\" />", ".axaml")]
    [InlineData("<BrushTransition Duration=\"00:00:00.180\" />", ".axaml")]
    [InlineData("<Animation Delay=\"0.1\" />", ".axaml")]
    [InlineData("var c = Color.FromRgb(23, 23, 26);", ".cs")]
    [InlineData("var c = Color.FromArgb(0x1A, 1, 2, 3);", ".cs")]
    [InlineData("var c = Color.Parse(\"#17171A\");", ".cs")]
    [InlineData("var b = Brushes.White;", ".cs")]
    [InlineData("var c = Colors.Red;", ".cs")]
    [InlineData("var d = TimeSpan.FromMilliseconds(180);", ".cs")]
    [InlineData("var d = TimeSpan.FromSeconds(0.3);", ".cs")]
    [InlineData("var s = \"#2E2E33\";", ".cs")]
    public void Scanner_flags_literals(string line, string extension) => Assert.NotEmpty(Scan(line, extension));

    [Theory]
    [InlineData("<Border Background=\"{DynamicResource Brush.Bg.Card}\" />", ".axaml")]
    [InlineData("<Border Background=\"Transparent\" />", ".axaml")]
    [InlineData("<FontFamily>avares://Doba/Assets/Fonts#Geist</FontFamily>", ".axaml")]
    [InlineData("<BrushTransition Duration=\"{x:Static m:MotionTimes.Hover}\" />", ".axaml")]
    [InlineData("Animator.FadeIn(host, Motion.Section);", ".cs")]
    [InlineData("var d = Motion.Ms(Motion.Hover);", ".cs")]
    [InlineData("#region Helpers", ".cs")]
    [InlineData("#define DEBUG_ADD", ".cs")]
    [InlineData("var tag = \"#\" + name;", ".cs")]
    public void Scanner_accepts_tokens(string line, string extension) => Assert.Empty(Scan(line, extension));

    /// <summary>
    /// Exact, reviewed exceptions: clock arithmetic that is not a colour or a motion duration. Keep this list tiny;
    /// prefer a named constant in the owning file (then delete the entry).
    /// </summary>
    private static readonly (string File, string Fragment)[] s_allowed =
    [
        // Minute-boundary scheduling: seconds left in the current minute + a 50 ms timer slack (not an animation).
        ("Services/DayClock.cs", "TimeSpan.FromSeconds(60 - t.Second)"),
        ("Services/DayClock.cs", "TimeSpan.FromMilliseconds(50)"),
    ];

    private static bool IsAllowed(string relativeFile, string line) =>
        s_allowed.Any(a => a.File == relativeFile && line.Contains(a.Fragment, StringComparison.Ordinal));

    /// <summary>The .axaml and .cs files under <paramref name="appRoot"/> except Theme/, Motion/, Platform/, bin/ and obj/.</summary>
    internal static IEnumerable<string> SourceFiles(string appRoot)
    {
        // Platform/ is native interop and audio synthesis (numeric constants, no UI colours or motion).
        string[] excluded = ["Theme", "Motion", "Platform", "bin", "obj"];
        return Directory.EnumerateFiles(appRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f =>
            {
                var first = Path.GetRelativePath(appRoot, f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                return !excluded.Contains(first, StringComparer.OrdinalIgnoreCase);
            })
            .Order(StringComparer.Ordinal);
    }

    internal static IEnumerable<string> Scan(string line, string extension)
    {
        if (HexColour().Match(line) is { Success: true } hex)
        {
            yield return "colour literal " + hex.Value;
        }

        if (ColorFactory().Match(line) is { Success: true } factory)
        {
            yield return "colour factory with literal " + factory.Value;
        }

        if (NamedColour().Match(line) is { Success: true } named)
        {
            yield return "named colour " + named.Value;
        }

        if (TimeSpanLiteral().Match(line) is { Success: true } span)
        {
            yield return "duration literal " + span.Value;
        }

        if (ClockString().Match(line) is { Success: true } clock)
        {
            yield return "duration string " + clock.Value;
        }

        if (extension.Equals(".axaml", StringComparison.OrdinalIgnoreCase))
        {
            if (DurationAttribute().Match(line) is { Success: true } attr)
            {
                yield return "duration attribute " + attr.Value;
            }

            if (NamedColourAttribute().Match(line) is { Success: true } attrColour)
            {
                yield return "named colour attribute " + attrColour.Value;
            }
        }
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Doba.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (Doba.sln) not found.");
    }

    // #RGB, #RGBA, #RRGGBB, #AARRGGBB not followed by another word character (so "#define", "#Geist" don't match).
    [GeneratedRegex(@"#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{3,4})(?![0-9A-Za-z_])")]
    private static partial Regex HexColour();

    [GeneratedRegex(@"\bColor\.(?:FromRgb|FromArgb|FromUInt32|Parse|TryParse|FromHsl|FromHsv)\s*\(\s*(?:\d|0x|""|-)")]
    private static partial Regex ColorFactory();

    [GeneratedRegex(@"\b(?:Colors|Brushes)\.[A-Z][A-Za-z]+")]
    private static partial Regex NamedColour();

    [GeneratedRegex(@"\b(?:TimeSpan\.From(?:Milliseconds|Seconds|Ticks|Minutes)\s*\(\s*[\d.]|new\s+TimeSpan\s*\(\s*\d)")]
    private static partial Regex TimeSpanLiteral();

    [GeneratedRegex(@"""\d{1,2}:\d{1,2}:\d{1,2}(?:\.\d+)?""")]
    private static partial Regex ClockString();

    [GeneratedRegex(@"\b(?:Duration|Delay)\s*=\s*""(?!\{)[^""]*""")]
    private static partial Regex DurationAttribute();

    [GeneratedRegex(@"\b(?:Background|Foreground|BorderBrush|Fill|Stroke|Color|CaretBrush|SelectionBrush|SelectionForegroundBrush|PlaceholderForeground|OpacityMask)\s*=\s*""(?!\{|Transparent""|\{x:Null\})[A-Za-z]+""")]
    private static partial Regex NamedColourAttribute();
}
