using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sati.Data;
using Sati.Models;
using Sati.Models.Assessments;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sati.Services.LocalAi
{
    public sealed partial class ClientAiContextService : IClientAiContextService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private static readonly HashSet<string> SearchStopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "about", "after", "again", "also", "been", "before", "being", "between",
            "call", "called", "case", "client", "community", "consumer", "could", "email",
            "from", "have", "into", "manager", "note", "other", "placed", "regarding",
            "should", "their", "there", "these", "they", "this", "through", "today",
            "with", "would"
        };

        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly LocalAiOptions _options;

        public ClientAiContextService(
            IDbContextFactory<SatiContext> contextFactory,
            IOptions<LocalAiOptions> options)
        {
            _contextFactory = contextFactory;
            _options = options.Value;
        }

        public async Task<ClientAiContext> BuildAsync(
            int personId,
            int requestingUserId,
            string roughNarrative,
            int? excludedNoteId = null,
            CancellationToken cancellationToken = default)
        {
            if (personId <= 0)
                throw new ArgumentOutOfRangeException(nameof(personId));
            if (requestingUserId <= 0)
                throw new InvalidOperationException("A signed-in user is required to assemble client context.");

            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // Projection is intentional: Journal, address, phone, MaineCare ID,
            // diagnosis, place of service, and billing fields never leave SQL.
            var profile = await db.People
                .AsNoTracking()
                .Where(person => person.Id == personId && person.UserId == requestingUserId)
                .Select(person => new ClientProfile
                {
                    Id = person.Id,
                    FirstName = person.FirstName,
                    LastName = person.LastName,
                    Bio = person.Bio,
                    Waiver = person.Waiver,
                    EffectiveDate = person.EffectiveDate,
                    HasGuardian = person.HasGuardian,
                    GuardianName = person.GuardianName,
                    PrimaryCareProvider = person.PrimaryCareProvider,
                    HealthcareSystemName = person.HealthcareSystemName,
                    OpenWithVr = person.OpenWithVR,
                    HasHomeSupport = person.HasHomeSupport,
                    HasSelfDirectedHomeSupport = person.HasSelfDirectedHomeSupport,
                    HasSharedLiving = person.HasSharedLiving,
                    HasCommunitySupport1To1 = person.HasCommunitySupport1To1,
                    HasCommunitySupportSelfDirected = person.HasCommunitySupportSelfDirected,
                    HasCommunitySupportDayProgram = person.HasCommunitySupportDayProgram,
                    DayProgramCount = person.DayProgramCount,
                    HasEmploymentSpecialist = person.HasEmploymentSpecialist,
                    HasWorkSupports = person.HasWorkSupports,
                    IsEmployed = person.IsEmployed
                })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new UnauthorizedAccessException(
                    "The selected client is not assigned to the signed-in user, so Sati did not provide client context to the AI assistant.");

            var forms = await db.Forms
                .AsNoTracking()
                .Where(form => form.PersonId == personId)
                .OrderBy(form => form.DueDate)
                .Select(form => new FormContextRow(
                    form.Id,
                    form.Type,
                    form.DueDate,
                    form.IsCompliant,
                    form.CompletedDate))
                .ToListAsync(cancellationToken);

            var recentNoteCount = Math.Clamp(_options.RecentContextNoteCount, 1, 25);
            var matchingNoteCount = Math.Clamp(_options.MatchingContextNoteCount, 0, 15);

            var noteQuery = db.Notes
                .AsNoTracking()
                .Where(note => note.PersonId == personId)
                .Where(note => note.Status != NoteStatus.Cancelled && note.Status != NoteStatus.Abandoned);

            if (excludedNoteId.HasValue)
                noteQuery = noteQuery.Where(note => note.Id != excludedNoteId.Value);

            var recentNotes = await noteQuery
                .OrderByDescending(note => note.EventDate)
                .ThenByDescending(note => note.Id)
                .Take(recentNoteCount)
                .Select(note => new NoteContextRow(
                    note.Id,
                    note.Narrative,
                    note.EventDate,
                    note.Status,
                    note.NoteType))
                .ToListAsync(cancellationToken);

            var recentIds = recentNotes.Select(note => note.Id).ToHashSet();
            var matchingNotes = matchingNoteCount == 0
                ? []
                : await FindMatchingNotesAsync(
                    noteQuery,
                    recentIds,
                    roughNarrative,
                    matchingNoteCount,
                    cancellationToken);

            var assessment = await GetContextAssessmentAsync(
                db,
                personId,
                requestingUserId,
                cancellationToken);

            return ComposeContext(profile, forms, recentNotes, matchingNotes, assessment);
        }

        private async Task<List<NoteContextRow>> FindMatchingNotesAsync(
            IQueryable<Note> noteQuery,
            HashSet<int> recentIds,
            string roughNarrative,
            int maximum,
            CancellationToken cancellationToken)
        {
            var terms = SearchTermRegex().Matches(roughNarrative ?? string.Empty)
                .Select(match => match.Value)
                .Where(term => term.Length >= 4 && !SearchStopWords.Contains(term))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();

            if (terms.Count == 0)
                return [];

            var candidates = new Dictionary<int, NoteContextRow>();
            foreach (var term in terms)
            {
                var matches = await noteQuery
                    .Where(note => !recentIds.Contains(note.Id) && note.Narrative.Contains(term))
                    .OrderByDescending(note => note.EventDate)
                    .ThenByDescending(note => note.Id)
                    .Take(3)
                    .Select(note => new NoteContextRow(
                        note.Id,
                        note.Narrative,
                        note.EventDate,
                        note.Status,
                        note.NoteType))
                    .ToListAsync(cancellationToken);

                foreach (var match in matches)
                    candidates.TryAdd(match.Id, match);
            }

            return candidates.Values
                .OrderByDescending(note => ScoreNote(note.Narrative, terms))
                .ThenByDescending(note => note.EventDate)
                .Take(maximum)
                .ToList();
        }

        private static int ScoreNote(string narrative, IEnumerable<string> terms) =>
            terms.Count(term => narrative.Contains(term, StringComparison.OrdinalIgnoreCase));

        private static async Task<AssessmentContextRow?> GetContextAssessmentAsync(
            SatiContext db,
            int personId,
            int requestingUserId,
            CancellationToken cancellationToken)
        {
            // The author's active working version is more current for that author.
            // Everyone else receives only the latest approved version.
            var authoredDraft = await db.ComprehensiveAssessments
                .AsNoTracking()
                .Where(item => item.PersonId == personId && item.AuthorUserId == requestingUserId)
                .Where(item => item.Status == AssessmentStatus.Draft || item.Status == AssessmentStatus.Returned)
                .OrderByDescending(item => item.Version)
                .Select(item => new AssessmentContextRow(
                    item.Id,
                    item.Version,
                    item.Status,
                    item.UpdatedAt,
                    item.DocumentJson))
                .FirstOrDefaultAsync(cancellationToken);

            if (authoredDraft is not null)
                return authoredDraft;

            return await db.ComprehensiveAssessments
                .AsNoTracking()
                .Where(item => item.PersonId == personId && item.Status == AssessmentStatus.Approved)
                .OrderByDescending(item => item.Version)
                .Select(item => new AssessmentContextRow(
                    item.Id,
                    item.Version,
                    item.Status,
                    item.UpdatedAt,
                    item.DocumentJson))
                .FirstOrDefaultAsync(cancellationToken);
        }

        private ClientAiContext ComposeContext(
            ClientProfile profile,
            IReadOnlyList<FormContextRow> forms,
            IReadOnlyList<NoteContextRow> recentNotes,
            IReadOnlyList<NoteContextRow> matchingNotes,
            AssessmentContextRow? assessment)
        {
            var sources = new List<ClientAiContextSource>();
            var text = new StringBuilder();

            text.AppendLine("CLIENT BACKGROUND SNAPSHOT");
            text.AppendLine($"Generated locally: {DateTime.Now:MMMM d, yyyy h:mm tt}");
            text.AppendLine();
            text.AppendLine("PROFILE");
            text.AppendLine($"Name: {profile.FullName}");
            text.AppendLine($"General bio: {ValueOrUnknown(profile.Bio)}");
            text.AppendLine($"Waiver: {profile.Waiver}");
            text.AppendLine($"Effective date: {FormatDate(profile.EffectiveDate)}");
            if (profile.HasGuardian)
                text.AppendLine($"Guardian: {ValueOrUnknown(profile.GuardianName)}");
            if (!string.IsNullOrWhiteSpace(profile.PrimaryCareProvider))
                text.AppendLine($"Primary care provider: {profile.PrimaryCareProvider.Trim()}");
            if (!string.IsNullOrWhiteSpace(profile.HealthcareSystemName))
                text.AppendLine($"Healthcare system: {profile.HealthcareSystemName.Trim()}");
            sources.Add(new("Profile", "Current client profile, including general Bio"));

            var services = BuildServiceList(profile);
            text.AppendLine();
            text.AppendLine("RECORDED SERVICES AND EMPLOYMENT");
            if (services.Count == 0)
                text.AppendLine("No active service flags are recorded.");
            else
                foreach (var service in services)
                    text.AppendLine($"- {service}");
            sources.Add(new("Services", "Current service and employment flags"));

            var relevantForms = SelectRelevantForms(forms);
            text.AppendLine();
            text.AppendLine("FORM AND REVIEW DEADLINES");
            foreach (var form in relevantForms)
            {
                var completion = form.IsCompliant
                    ? $"complete{(form.CompletedDate.HasValue ? $" on {form.CompletedDate:MMMM d, yyyy}" : string.Empty)}"
                    : form.DueDate.Date < DateTime.Today ? "overdue and incomplete" : "incomplete";
                text.AppendLine($"- {Person.FormDisplayName(form.Type)}: due {form.DueDate:MMMM d, yyyy}; {completion}");
                sources.Add(new("Deadline", $"{Person.FormDisplayName(form.Type)} due {form.DueDate:MMM d, yyyy}"));
            }

            if (assessment is not null)
                AppendAssessment(text, sources, assessment);

            text.AppendLine();
            text.AppendLine("RECENT CASE NOTES (historical background; not current-contact evidence)");
            foreach (var note in recentNotes)
                AppendNote(text, sources, note, "Recent note");

            if (matchingNotes.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("OLDER TOPIC-MATCHED NOTES (historical background; not current-contact evidence)");
                foreach (var note in matchingNotes)
                    AppendNote(text, sources, note, "Topic match");
            }

            var maximum = Math.Clamp(_options.MaxContextCharacters, 4_000, 60_000);
            var promptText = text.ToString().Trim();
            if (promptText.Length > maximum)
                promptText = promptText[..maximum].TrimEnd() + "\n[Context truncated at the configured local limit.]";

            return new ClientAiContext(promptText, sources);
        }

        private void AppendAssessment(
            StringBuilder text,
            ICollection<ClientAiContextSource> sources,
            AssessmentContextRow assessment)
        {
            AssessmentDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<AssessmentDocument>(assessment.DocumentJson, JsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (document is null)
                return;

            var section = new StringBuilder();
            section.AppendLine();
            section.AppendLine($"COMPREHENSIVE ASSESSMENT VERSION {assessment.Version} ({assessment.Status})");

            if (document.Contributors.Count > 0)
            {
                section.AppendLine("Contributors:");
                foreach (var contributor in document.Contributors)
                    section.AppendLine($"- {contributor.Name}: {contributor.Relationship}");
            }

            foreach (var (key, answer) in document.Answers.OrderBy(item => item.Key))
            {
                if (string.IsNullOrWhiteSpace(answer.Narrative) &&
                    string.IsNullOrWhiteSpace(answer.SupportDetails) &&
                    string.IsNullOrWhiteSpace(answer.ExceptionReason) &&
                    string.IsNullOrWhiteSpace(answer.DissentingOpinion))
                    continue;

                section.AppendLine($"Assessment item '{HumanizeKey(key)}' — status {answer.Status}:");
                AppendIfPresent(section, "Team assessment", answer.Narrative);
                if (answer.Supports != SupportMethod.None)
                    section.AppendLine($"  Support methods: {answer.Supports}");
                AppendIfPresent(section, "Support details", answer.SupportDetails);
                AppendIfPresent(section, "Status explanation", answer.ExceptionReason);
                AppendIfPresent(section, "Dissenting opinion", answer.DissentingOpinion);
            }

            if (document.Needs.Count > 0)
            {
                section.AppendLine("Recorded needs:");
                foreach (var need in document.Needs)
                {
                    section.AppendLine($"- {need.Type}: {need.Description}");
                    AppendIfPresent(section, "Desired result", need.DesiredResult);
                    if (need.AssociateProvider && !string.IsNullOrWhiteSpace(need.ProviderNameSnapshot))
                        section.AppendLine($"  Associated provider: {need.ProviderNameSnapshot}");
                }
            }

            var assessmentText = section.ToString();
            var maximum = Math.Clamp(_options.MaxAssessmentContextCharacters, 1_000, 16_000);
            if (assessmentText.Length > maximum)
                assessmentText = assessmentText[..maximum].TrimEnd() + "\n[Assessment context truncated.]\n";

            text.Append(assessmentText);
            sources.Add(new(
                "Assessment",
                $"Comprehensive Assessment v{assessment.Version}, {assessment.Status}, updated {assessment.UpdatedAt.ToLocalTime():MMM d, yyyy}"));
        }

        private void AppendNote(
            StringBuilder text,
            ICollection<ClientAiContextSource> sources,
            NoteContextRow note,
            string category)
        {
            var date = note.EventDate?.ToString("MMMM d, yyyy") ?? "date not recorded";
            var excerpt = CollapseWhitespace(note.Narrative);
            var maximum = Math.Clamp(_options.MaxContextNoteCharacters, 300, 2_500);
            if (excerpt.Length > maximum)
                excerpt = excerpt[..maximum].TrimEnd() + "…";

            text.AppendLine($"- Note #{note.Id}; {date}; {note.NoteType?.ToString() ?? "type not recorded"}; {note.Status?.ToString() ?? "status not recorded"}");
            text.AppendLine($"  {excerpt}");
            sources.Add(new(category, $"Case note #{note.Id}, {date}"));
        }

        private static List<string> BuildServiceList(ClientProfile profile)
        {
            var services = new List<string>();
            AddIf(profile.HasHomeSupport, "Home Support", services);
            AddIf(profile.HasSelfDirectedHomeSupport, "Self-Directed Home Support", services);
            AddIf(profile.HasSharedLiving, "Shared Living", services);
            AddIf(profile.HasCommunitySupport1To1, "Community Support (1:1)", services);
            AddIf(profile.HasCommunitySupportSelfDirected, "Self-Directed Community Support", services);
            AddIf(
                profile.HasCommunitySupportDayProgram,
                $"Community Support day program ({Math.Max(1, profile.DayProgramCount)} recorded program(s))",
                services);
            AddIf(profile.HasEmploymentSpecialist, "Employment Specialist", services);
            AddIf(profile.HasWorkSupports, "Work Supports", services);
            AddIf(profile.OpenWithVr, "Open Vocational Rehabilitation case", services);
            AddIf(profile.IsEmployed, "Currently employed", services);
            return services;
        }

        private static IReadOnlyList<FormContextRow> SelectRelevantForms(IReadOnlyList<FormContextRow> forms)
        {
            var today = DateTime.Today;
            return forms
                .Where(form => !form.IsCompliant || form.DueDate.Date >= today.AddDays(-120))
                .OrderBy(form => form.IsCompliant)
                .ThenBy(form => form.DueDate)
                .Take(16)
                .OrderBy(form => form.DueDate)
                .ToList();
        }

        private static void AddIf(bool condition, string value, ICollection<string> destination)
        {
            if (condition)
                destination.Add(value);
        }

        private static void AppendIfPresent(StringBuilder text, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                text.AppendLine($"  {label}: {CollapseWhitespace(value)}");
        }

        private static string ValueOrUnknown(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "Not recorded" : CollapseWhitespace(value);

        private static string FormatDate(DateTime? value) =>
            value?.ToString("MMMM d, yyyy") ?? "Not recorded";

        private static string HumanizeKey(string value) =>
            Regex.Replace(value.Replace('-', ' ').Replace('_', ' '), @"\s+", " ").Trim();

        private static string CollapseWhitespace(string value) =>
            WhitespaceRegex().Replace(value.Trim(), " ");

        [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’-]*")]
        private static partial Regex SearchTermRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRegex();

        private sealed class ClientProfile
        {
            public int Id { get; init; }
            public string? FirstName { get; init; }
            public string? LastName { get; init; }
            public string? Bio { get; init; }
            public WaiverType Waiver { get; init; }
            public DateTime? EffectiveDate { get; init; }
            public bool HasGuardian { get; init; }
            public string? GuardianName { get; init; }
            public string? PrimaryCareProvider { get; init; }
            public string? HealthcareSystemName { get; init; }
            public bool OpenWithVr { get; init; }
            public bool HasHomeSupport { get; init; }
            public bool HasSelfDirectedHomeSupport { get; init; }
            public bool HasSharedLiving { get; init; }
            public bool HasCommunitySupport1To1 { get; init; }
            public bool HasCommunitySupportSelfDirected { get; init; }
            public bool HasCommunitySupportDayProgram { get; init; }
            public int DayProgramCount { get; init; }
            public bool HasEmploymentSpecialist { get; init; }
            public bool HasWorkSupports { get; init; }
            public bool IsEmployed { get; init; }
            public string FullName => $"{FirstName} {LastName}".Trim();
        }

        private sealed record FormContextRow(
            int Id,
            FormType Type,
            DateTime DueDate,
            bool IsCompliant,
            DateTime? CompletedDate);

        private sealed record NoteContextRow(
            int Id,
            string Narrative,
            DateTime? EventDate,
            NoteStatus? Status,
            NoteType? NoteType);

        private sealed record AssessmentContextRow(
            int Id,
            int Version,
            AssessmentStatus Status,
            DateTime UpdatedAt,
            string DocumentJson);
    }
}
