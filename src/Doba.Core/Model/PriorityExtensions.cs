namespace Doba.Core.Model;

public static class PriorityExtensions
{
    /// <summary>Next priority in the click cycle None → Low → Medium → High → Asap → None.</summary>
    public static Priority Next(this Priority p) => (Priority)(((int)p + 1) % 5);

    /// <summary>"Low", "Medium", "High", "ASAP"; empty for None.</summary>
    public static string DisplayName(this Priority p) => p switch
    {
        Priority.Low => "Low",
        Priority.Medium => "Medium",
        Priority.High => "High",
        Priority.Asap => "ASAP",
        _ => string.Empty,
    };
}
