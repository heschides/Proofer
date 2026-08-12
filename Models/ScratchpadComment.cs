using System.ComponentModel.DataAnnotations.Schema;

namespace Sati.Models
{
    /// <summary>
    /// An append-only comment added after a scratchpad's calendar day ended.
    /// The author name is snapshotted so later profile edits do not rewrite history.
    /// </summary>
    public sealed class ScratchpadComment
    {
        public int Id { get; set; }
        public int ScratchpadId { get; set; }
        public Scratchpad Scratchpad { get; set; } = null!;
        public int AuthorUserId { get; set; }
        public string AuthorDisplayName { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public string Content { get; set; } = string.Empty;

        [NotMapped]
        public DateTime CreatedAtLocal => CreatedAtUtc.ToLocalTime();

        [NotMapped]
        public string DisplayHeader =>
            $"ADDED LATER • {CreatedAtLocal:MMM d, yyyy 'at' h:mm tt} • {AuthorDisplayName}";
    }
}
