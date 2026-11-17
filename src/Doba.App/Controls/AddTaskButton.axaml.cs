using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Doba.App.Controls;

/// <summary>
/// The dashed "Add task" row of the Inbox, Today and Day lists (Button.Dashed: 38 px, dashed Border.Strong, r8,
/// 13.5 px Text.Muted, hover Overlay.Hover). Bind <see cref="Command"/> to a view-model command that calls
/// <c>IAppCommands.NewTask(date, hasExplicitDate: true)</c>.
/// </summary>
public partial class AddTaskButton : UserControl
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<AddTaskButton, ICommand?>(nameof(Command));

    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<AddTaskButton, object?>(nameof(CommandParameter));

    /// <summary>Label text; default "Add task".</summary>
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<AddTaskButton, string>(nameof(Label), "Add task");

    /// <summary>Keyboard hint shown at the right; default "N". Empty hides the key cap.</summary>
    public static readonly StyledProperty<string?> ShortcutProperty =
        AvaloniaProperty.Register<AddTaskButton, string?>(nameof(Shortcut), "N");

    public AddTaskButton() => InitializeComponent();

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Shortcut
    {
        get => GetValue(ShortcutProperty);
        set => SetValue(ShortcutProperty, value);
    }

    /// <summary>The inner button (tests, focus).</summary>
    public Button Button => PART_Button;
}
