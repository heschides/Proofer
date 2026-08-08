namespace Sati.ViewModels
{
    /// <summary>A visually distinct set of task-board items for one person.</summary>
    public sealed record BoardPersonGroup(
        string ClientName,
        IReadOnlyList<object> Items,
        bool IsAlternate);
}
