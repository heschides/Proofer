using System.Globalization;

namespace Sati.Contracts.V1;

/// <summary>
/// Sole owner of how a stamped entry is written into a consumer journal, shared
/// by <c>Sati.Api</c> and the desktop's transitional local path so the two cannot
/// stamp or order entries two different ways.
///
/// A journal is free text a case manager edits directly, so an entry written by
/// the application has to be recognizable after the fact: the stamp line carries
/// the date, the time, and the kind of entry, and the narrative follows on its own
/// lines. Newest first, because the journal is read from the top.
///
/// The stamp is composed by the WRITER of the record, not by the caller that
/// supplied the text. On the server that is <c>ApiClock</c> in agency-local time —
/// an Azure host's own local time is UTC and would stamp several hours off the
/// wall clock the case manager just read. The caller never sends a timestamp,
/// because a client-supplied time would let the record claim a moment that did
/// not happen.
/// </summary>
public static class JournalEntry
{
    /// <summary>Marks the entry's kind on the stamp line.</summary>
    public const string ReminderLabel = "REMINDER";

    /// <summary>
    /// Upper bound on one entry's text. The journal column itself is unbounded,
    /// so the limit exists to keep a single request from writing an arbitrarily
    /// large body into a consumer record; it is a contract constant so the client
    /// refuses the same length the server refuses.
    /// </summary>
    public const int MaxTextLength = 4000;

    // Invariant so the stamp does not change shape with the machine's locale.
    // A journal is read by more than the person who wrote it.
    private const string StampFormat = "MMMM d, yyyy h:mm tt";

    /// <summary>The stamp line and narrative for a reminder, without surrounding blank lines.</summary>
    /// <exception cref="ArgumentException">The text is empty, whitespace, or longer than <see cref="MaxTextLength"/>.</exception>
    public static string ComposeReminder(DateTime stampedAt, string text)
    {
        var body = Normalize(text);
        if (body.Length == 0)
            throw new ArgumentException("A reminder needs text.", nameof(text));
        if (body.Length > MaxTextLength)
            throw new ArgumentException(
                $"A reminder is limited to {MaxTextLength} characters.", nameof(text));

        var stamp = stampedAt.ToString(StampFormat, CultureInfo.InvariantCulture);
        return $"{stamp} — {ReminderLabel}\r\n{body}";
    }

    /// <summary>
    /// Places <paramref name="entry"/> at the top of the journal, separated from
    /// existing content by one blank line. Existing text is preserved as written
    /// apart from leading blank lines, which would otherwise accumulate at the
    /// seam with every entry.
    /// </summary>
    public static string Prepend(string? existingJournal, string entry)
    {
        var existing = Normalize(existingJournal ?? string.Empty).TrimStart('\r', '\n');
        return existing.Length == 0 ? entry : $"{entry}\r\n\r\n{existing}";
    }

    /// <summary>
    /// The single call both write paths use: compose the stamped reminder and put
    /// it at the top of the journal. Returns the journal's new full text.
    /// </summary>
    public static string PrependReminder(string? existingJournal, DateTime stampedAt, string text) =>
        Prepend(existingJournal, ComposeReminder(stampedAt, text));

    // The journal is edited in a WPF TextBox, which writes \r\n. Text arriving
    // over HTTP from any other source is brought to the same line endings so the
    // seam between a new entry and existing content is not mixed.
    private static string Normalize(string value) => value
        .Replace("\r\n", "\n")
        .Replace('\r', '\n')
        .Replace("\n", "\r\n")
        .Trim();
}
