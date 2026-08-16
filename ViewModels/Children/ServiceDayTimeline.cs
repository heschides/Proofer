using Sati.Contracts.V1;

namespace Sati.ViewModels.Children
{
    // Presentation shapes for the service-day bar. The RULE lives in
    // Sati.Contracts.V1.ServiceTimeline and is shared with the API; these types
    // only describe how a day is drawn and labelled.

    public enum ServiceDaySegmentKind
    {
        // Time already committed by another note on this date.
        Recorded,

        // The note currently being written, fitting in free time.
        Draft,

        // The note currently being written, colliding with recorded time.
        Conflict
    }

    /// <summary>One selectable start time, stored as minutes from 7:00 AM.</summary>
    public sealed record ServiceStartOption(int Minutes, string Display)
    {
        public override string ToString() => Display;

        /// <summary>Every slot from 7:00 AM through the last one that still fits inside the day.</summary>
        public static IReadOnlyList<ServiceStartOption> BuildDay()
        {
            var options = new List<ServiceStartOption>();
            for (var minutes = 0; minutes < ServiceTimeline.WindowLengthMinutes; minutes += ServiceTimeline.SlotMinutes)
                options.Add(new ServiceStartOption(minutes, ServiceTimeline.Describe(minutes)));
            return options;
        }
    }

    /// <summary>
    /// A drawn band on the bar. Fractions are 0–1 across the 7:00 AM – 7:00 PM
    /// window so the bar can be scaled to whatever width the panel gives it.
    /// </summary>
    public sealed record ServiceDaySegment(
        double StartFraction,
        double WidthFraction,
        string Label,
        ServiceDaySegmentKind Kind)
    {
        // Color alone never carries the meaning of a band. This text is the
        // tooltip and the accessible name, and the panel repeats the verdict in
        // a sentence below the bar.
        public string Description => Kind switch
        {
            ServiceDaySegmentKind.Draft => $"This note: {Label}",
            ServiceDaySegmentKind.Conflict => $"Overlapping time: {Label}",
            _ => $"Already recorded: {Label}"
        };

        public static ServiceDaySegment FromBlock(ServiceBlock block, ServiceDaySegmentKind kind)
        {
            var total = (double)ServiceTimeline.WindowLengthMinutes;
            var start = Math.Clamp(block.StartMinutes / total, 0d, 1d);
            var width = Math.Clamp(block.Minutes / total, 0d, 1d - start);
            var who = string.IsNullOrWhiteSpace(block.Label) ? string.Empty : $" — {block.Label}";
            return new ServiceDaySegment(start, width, $"{ServiceTimeline.DescribeRange(block)}{who}", kind);
        }
    }
}
