using System.Text.RegularExpressions;

namespace Doba.App.Tests.DesignSystem;

/// <summary>
/// Every colour comes from Theme/Tokens.axaml and every duration and easing from Motion: no colour, duration, delay or
/// easing literals anywhere in src/Doba.App except the files that define those tokens (and Platform/'s native code).
/// Screen and control markup also takes its spacing, sizes, radii and font sizes from tokens (0 / ±1 px allowed).
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

                if (IsLayoutChecked(relative))
                {
                    foreach (var finding in ScanLayout(lines[i]))
                    {
                        violations.Add($"{Path.GetRelativePath(RepoRoot(), file)}:{i + 1}: {finding}  →  {lines[i].Trim()}");
                    }
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

        // Theme and Motion are checked too, except the two files that define the palette and the motion tokens.
        Assert.Contains("Theme/Styles.axaml", files);
        Assert.Contains("Theme/ThemeService.cs", files);
        Assert.Contains("Theme/Controls/Buttons.axaml", files);
        Assert.Contains("Theme/Gallery/ControlsGallery.axaml", files);
        Assert.Contains("Motion/Animator.cs", files);
        Assert.Contains("Motion/MotionSync.cs", files);
        foreach (var exempt in s_definitions)
        {
            Assert.DoesNotContain(exempt, files);
        }

        Assert.DoesNotContain(files, f => f.StartsWith("Platform/", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.StartsWith("obj/", StringComparison.Ordinal) || f.StartsWith("bin/", StringComparison.Ordinal));
    }

    [Fact]
    public void Screens_and_controls_markup_is_checked_for_layout_literals()
    {
        Assert.True(IsLayoutChecked("Screens/Week/WeekRowView.axaml"));
        Assert.True(IsLayoutChecked("Controls/TaskListRow.axaml"));
        Assert.False(IsLayoutChecked("Controls/TaskListRow.axaml.cs"));
        Assert.False(IsLayoutChecked("Theme/Controls/Buttons.axaml"));
    }

    [Theory]
    [InlineData("<StackPanel Spacing=\"28\" />")]
    [InlineData("<Grid ColumnSpacing=\"48\">")]
    [InlineData("<Border Padding=\"14,2,14,0\" />")]
    [InlineData("<TextBlock Margin=\"0,-5,0,5\" />")]
    [InlineData("<Button CornerRadius=\"11\" />")]
    [InlineData("<TextBlock FontSize=\"13\" />")]
    [InlineData("<TextBlock LineHeight=\"16\" />")]
    [InlineData("<Panel Width=\"22\" Height=\"22\" />")]
    [InlineData("<Border MinHeight=\"66\" />")]
    [InlineData("<Border BorderThickness=\"1.5\" />")]
    [InlineData("<c:Icon Size=\"11\" StrokeWidth=\"2\" />")]
    [InlineData("<Grid ColumnDefinitions=\"40,*\">")]
    [InlineData("<Setter Property=\"Margin\" Value=\"6,0\" />")]
    public void Layout_scanner_flags_literals(string line) => Assert.NotEmpty(ScanLayout(line));

    [Theory]
    [InlineData("<StackPanel Spacing=\"{StaticResource Space.List.SectionGap}\" />")]
    [InlineData("<Border BorderThickness=\"0,0,0,1\" />")]                   // 1 px rules
    [InlineData("<c:TaskCheckBox Margin=\"0,0,0,1\" />")]                    // 0 / ±1 px nudges
    [InlineData("<Border Margin=\"-1,0,0,0\" Height=\"1\" />")]
    [InlineData("<ColumnDefinition Width=\"1000*\" />")]                     // star weights
    [InlineData("<Grid ColumnDefinitions=\"Auto,*,Auto\">")]
    [InlineData("<Grid Grid.Column=\"2\" ZIndex=\"2\">")]                     // not sizes
    [InlineData("<c:TaskCheckBox BoxSize=\"14\" />")]                        // a control variant, matched by its theme
    [InlineData("<Setter Property=\"Padding\" Value=\"0\" />")]
    [InlineData("<TextBlock MaxLines=\"2\" />")]
    public void Layout_scanner_accepts_tokens_and_hairlines(string line) => Assert.Empty(ScanLayout(line));

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
    [InlineData("<Setter Property=\"Background\" Value=\"White\" />", ".axaml")]
    [InlineData("<Setter Property=\"Foreground\" Value=\"Red\"/>", ".axaml")]
    [InlineData("var c = new Color(255, 23, 23, 26);", ".cs")]
    [InlineData("var b = new SolidColorBrush(0xFF17171A);", ".cs")]
    [InlineData("<DoubleTransition Easing=\"0.25,0.1,0.25,1\" />", ".axaml")]
    [InlineData("<Animation Easing=\"CubicEaseOut\" />", ".axaml")]
    [InlineData("<KeyFrame KeySpline=\"0.42,0,1,1\" />", ".axaml")]
    [InlineData("var e = new SplineEasing(0.22, 1, 0.36, 1);", ".cs")]
    [InlineData("var e = new CubicEaseOut();", ".cs")]
    [InlineData("var s = new KeySpline(0.42, 0, 1, 1);", ".cs")]
    [InlineData("await Task.Delay(200);", ".cs")]
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
    [InlineData("<Setter Property=\"Background\" Value=\"Transparent\" />", ".axaml")]
    [InlineData("<Setter Property=\"HorizontalAlignment\" Value=\"Center\" />", ".axaml")]
    [InlineData("<DoubleTransition Easing=\"{x:Static m:Motion.EaseStd}\" />", ".axaml")]
    [InlineData("var target = new Color(ClearAlpha, c.R, c.G, c.B);", ".cs")]
    [InlineData("await Task.Delay(TimeSpan.FromMilliseconds(Motion.Exit));", ".cs")]
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

    /// <summary>The files that define the tokens themselves: the palette, the accent swatches and the motion tokens.</summary>
    private static readonly string[] s_definitions =
        ["Theme/Tokens.axaml", "Theme/AccentPalette.cs", "Motion/Motion.cs", "Motion/MotionTimes.cs"];

    /// <summary>
    /// The .axaml and .cs files under <paramref name="appRoot"/> except the token definitions (<see cref="s_definitions"/>),
    /// Platform/, bin/ and obj/.
    /// </summary>
    internal static IEnumerable<string> SourceFiles(string appRoot)
    {
        // Platform/ is native interop and audio synthesis (numeric constants, no UI colours or motion).
        string[] excluded = ["Platform", "bin", "obj"];
        return Directory.EnumerateFiles(appRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f =>
            {
                var relative = Path.GetRelativePath(appRoot, f).Replace('\\', '/');
                return !excluded.Contains(relative.Split('/')[0], StringComparer.OrdinalIgnoreCase)
                    && !s_definitions.Contains(relative, StringComparer.Ordinal);
            })
            .Order(StringComparer.Ordinal);
    }

    /// <summary>Screen and control markup: spacing, sizes, radii and font sizes come from Tokens.axaml too.</summary>
    internal static bool IsLayoutChecked(string relativeFile) =>
        relativeFile.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
        && (relativeFile.StartsWith("Screens/", StringComparison.Ordinal) || relativeFile.StartsWith("Controls/", StringComparison.Ordinal));

    /// <summary>
    /// Numeric layout values written in markup. Allowed: 0 and ±1 (hairline rules and one-pixel nudges), star weights
    /// and Auto; everything else must be a token.
    /// </summary>
    internal static IEnumerable<string> ScanLayout(string line)
    {
        foreach (Match m in LayoutAttribute().Matches(line))
        {
            if (HasLayoutNumber(m.Groups["value"].Value))
            {
                yield return "layout literal " + m.Value;
            }
        }

        foreach (Match m in LayoutSetter().Matches(line))
        {
            if (HasLayoutNumber(m.Groups["value"].Value))
            {
                yield return "layout literal " + m.Value;
            }
        }
    }

    private static bool HasLayoutNumber(string value) =>
        value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Any(part =>
            !part.EndsWith('*') && double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n)
            && n is not (0 or 1 or -1));

    internal static IEnumerable<string> Scan(string line, string extension)
    {
        if (ColorConstructor().Match(line) is { Success: true } ctor)
        {
            yield return "colour constructed from literals " + ctor.Value;
        }

        if (EasingConstruction().Match(line) is { Success: true } easing)
        {
            yield return "easing outside Motion " + easing.Value;
        }

        if (DelayLiteral().Match(line) is { Success: true } delay)
        {
            yield return "delay literal " + delay.Value;
        }

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

            if (NamedColourSetter().Match(line) is { Success: true } setterColour)
            {
                yield return "named colour setter " + setterColour.Value;
            }

            if (EasingAttribute().Match(line) is { Success: true } easingAttr)
            {
                yield return "easing attribute " + easingAttr.Value;
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

    // <Setter Property="Background" Value="White" /> — a named colour given to a brush or colour property in a style.
    [GeneratedRegex(@"\bProperty\s*=\s*""(?:Background|Foreground|BorderBrush|Fill|Stroke|Color|CaretBrush|SelectionBrush|SelectionForegroundBrush|PlaceholderForeground|OpacityMask)""\s+Value\s*=\s*""(?!\{|Transparent""|\{x:Null\})[A-Za-z]+""")]
    private static partial Regex NamedColourSetter();

    // new Color(255, …) / new SolidColorBrush(0xFF…): a colour built from numbers.
    [GeneratedRegex(@"\bnew\s+(?:Color|SolidColorBrush)\s*\(\s*(?:\d|0x)")]
    private static partial Regex ColorConstructor();

    // Curves come only from Motion: no easing objects or key splines constructed elsewhere.
    [GeneratedRegex(@"\bnew\s+\w*(?:Easing|EaseIn|EaseOut|EaseInOut)\s*\(|\bKeySpline\b")]
    private static partial Regex EasingConstruction();

    [GeneratedRegex(@"\bEasing\s*=\s*""(?!\{)[^""]*""")]
    private static partial Regex EasingAttribute();

    // Task.Delay(200): a wait in raw milliseconds.
    [GeneratedRegex(@"\bTask\.Delay\s*\(\s*\d")]
    private static partial Regex DelayLiteral();

    private const string LayoutNames =
        "Margin|Padding|Spacing|ColumnSpacing|RowSpacing|CornerRadius|FontSize|LineHeight|LetterSpacing|Width|Height|MinWidth|MinHeight|MaxWidth|MaxHeight|BorderThickness|Size|StrokeWidth|ColumnDefinitions|RowDefinitions";

    [GeneratedRegex(@"(?<![\w.])(?:" + LayoutNames + @")\s*=\s*""(?<value>[^""{]*\d[^""{]*)""")]
    private static partial Regex LayoutAttribute();

    [GeneratedRegex(@"\bProperty\s*=\s*""(?:" + LayoutNames + @")""\s+Value\s*=\s*""(?<value>[^""{]*\d[^""{]*)""")]
    private static partial Regex LayoutSetter();
}
