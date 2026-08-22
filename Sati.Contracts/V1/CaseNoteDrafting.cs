using System.Text.RegularExpressions;

namespace Sati.Contracts.V1;

[Flags]
public enum CaseNoteFactUsage
{
    None = 0,
    Narrative = 1,
    FollowUp = 2
}

/// <summary>
/// One current-contact fact supplied by the case manager or an explicit note-template control.
/// Facts have stable ids so model output can prove which inputs support each proposed sentence.
/// </summary>
public sealed record CaseNoteDraftFact(
    string Id,
    string Text,
    string Category,
    CaseNoteFactUsage Usage,
    bool Required = true,
    IReadOnlyList<string>? RequiredTerms = null);

/// <summary>
/// One proposed sentence and the exact current-contact fact ids claimed as its support.
/// Paragraph is presentation-only and must be monotonically nondecreasing.
/// </summary>
public sealed record CaseNoteDraftSentence(
    string Text,
    IReadOnlyList<string> FactIds,
    int Paragraph = 1);

public sealed record CaseNoteDraftPlan(
    IReadOnlyList<CaseNoteDraftSentence> Sentences,
    CaseNoteDraftSentence? FollowUp);

public sealed record CaseNoteDraftValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlySet<string> UsedFactIds)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Shared fail-closed rules for a grounded case-note plan. The model may propose prose, but it
/// cannot omit a required input, cite an unknown input, use a narrative-only fact as follow-up,
/// or introduce a protected name/number/quotation without a cited source.
/// </summary>
public static partial class CaseNoteDraftRules
{
    public const string NoFollowUpFactId = "SYSTEM-NO-FOLLOW-UP";
    public const string NoFollowUpText = "No follow-up was documented.";

    private static readonly HashSet<string> StructuralCapitalWords = new(StringComparer.Ordinal)
    {
        "After", "Before", "CCM", "Case", "Community", "Consumer", "During", "Following",
        "Manager", "Once", "The", "This"
    };

    private static readonly HashSet<string> CoverageStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "been", "being", "did", "do", "does", "follow-up", "followup", "had",
        "has", "have", "is", "it", "note", "that", "the", "their", "them", "these", "they",
        "this", "those", "was", "were"
    };

    private static readonly HashSet<string> StructuralVocabulary = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "as", "at", "been", "being", "but", "by", "case", "ccm", "consumer",
        "did", "do", "documented", "does", "for", "from", "had", "has", "have", "how", "if", "in", "into", "is",
        "it", "manager", "of", "on", "or", "than", "that", "the", "their", "them", "then", "these",
        "they", "this", "those", "to", "was", "were", "which", "who", "whose", "why", "with"
    };

    private static readonly string[][] SupportedWordFamilies =
    [
        ["call", "called", "calling", "contact", "contacted", "spoke", "speak", "communicated", "communication"],
        ["say", "said", "state", "stated", "report", "reported"],
        ["ask", "asked", "question", "questions"],
        ["meet", "met", "meeting", "visit", "visited"],
        ["review", "reviewed"],
        ["observe", "observed"],
        ["attend", "attended"],
        ["provide", "provided"],
        ["discuss", "discussed"],
        ["followup", "follow-up"]
    ];

    public static CaseNoteDraftValidationResult Validate(
        IReadOnlyList<CaseNoteDraftFact> facts,
        CaseNoteDraftPlan plan,
        IEnumerable<string>? trustedTerms = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(plan);

        var errors = new List<string>();
        var factById = new Dictionary<string, CaseNoteDraftFact>(StringComparer.Ordinal);
        foreach (var fact in facts)
        {
            if (string.IsNullOrWhiteSpace(fact.Id) || string.IsNullOrWhiteSpace(fact.Text))
            {
                errors.Add("Every drafting fact must have a nonblank id and text.");
                continue;
            }

            if (!factById.TryAdd(fact.Id, fact))
                errors.Add($"Drafting fact id '{fact.Id}' is duplicated.");
        }

        var trusted = string.Join("\n", trustedTerms ?? []).Trim();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var sentences = plan.Sentences ?? [];
        if (sentences.Count == 0)
            errors.Add("The grounded draft did not contain a narrative sentence.");
        else if (!sentences[0].Text.TrimStart().StartsWith("CCM ", StringComparison.Ordinal))
            errors.Add("The first grounded sentence must begin with 'CCM ' so Sati can render the required author envelope.");

        var previousParagraph = 1;
        for (var sentenceIndex = 0; sentenceIndex < sentences.Count; sentenceIndex++)
        {
            var sentence = sentences[sentenceIndex];
            if (sentence.Paragraph < 1 || sentence.Paragraph < previousParagraph ||
                (sentenceIndex == 0 && sentence.Paragraph != 1))
                errors.Add("Draft paragraph numbers must start at one and remain in order.");
            previousParagraph = Math.Max(previousParagraph, sentence.Paragraph);

            ValidateSentence(
                sentence,
                CaseNoteFactUsage.Narrative,
                factById,
                trusted,
                used,
                errors,
                isFollowUp: false);
        }

        if (plan.FollowUp is null)
        {
            errors.Add("The grounded draft did not provide the required Follow-up section.");
        }
        else
        {
            ValidateSentence(
                plan.FollowUp,
                CaseNoteFactUsage.FollowUp,
                factById,
                trusted,
                used,
                errors,
                isFollowUp: true);

            var hasExplicitFollowUp = facts.Any(fact =>
                fact.Required && fact.Usage.HasFlag(CaseNoteFactUsage.FollowUp));
            if (hasExplicitFollowUp && plan.FollowUp.FactIds.Contains(NoFollowUpFactId, StringComparer.Ordinal))
                errors.Add("The no-follow-up fallback cannot replace an explicit follow-up supplied by the case manager.");
        }

        foreach (var missing in facts.Where(fact => fact.Required && !used.Contains(fact.Id)))
            errors.Add($"Required current-note fact '{missing.Id}' was omitted from the draft.");

        var proposedSentences = sentences
            .Concat(plan.FollowUp is null ? [] : [plan.FollowUp])
            .ToList();
        foreach (var fact in facts.Where(fact => fact.Required && used.Contains(fact.Id)))
        {
            var supportedText = string.Join(" ", proposedSentences
                .Where(sentence => sentence.FactIds.Contains(fact.Id, StringComparer.Ordinal))
                .Select(sentence => sentence.Text));
            var requiredTerms = fact.RequiredTerms?.Where(term => !string.IsNullOrWhiteSpace(term)).ToList() ?? [];
            if (requiredTerms.Count == 0 && string.Equals(fact.Category, "Rough note", StringComparison.Ordinal))
            {
                var materialTerms = CoverageWordRegex().Matches(fact.Text)
                    .Select(match => match.Value)
                    .Where(term => !CoverageStopWords.Contains(term))
                    .ToList();
                var proposedWords = CoverageWordRegex().Matches(supportedText)
                    .Select(match => NormalizeWord(match.Value))
                    .ToList();
                var supportedWords = proposedWords.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missingTerms = materialTerms
                    .Where(term => !IsWordSupportedBy(term, supportedWords))
                    .ToList();
                if (missingTerms.Count > 0)
                {
                    errors.Add(
                        $"Required rough-note fragment '{fact.Id}' was cited but did not retain: " +
                        string.Join(", ", missingTerms.Select(term => $"'{term}'")) + ".");
                }
                else if (!AppearsInOrder(materialTerms, proposedWords))
                {
                    errors.Add(
                        $"Required rough-note fragment '{fact.Id}' did not preserve the order of its material terms and may have changed attribution or meaning.");
                }
            }
            else
            {
                foreach (var term in requiredTerms)
                {
                    if (!supportedText.Contains(term, StringComparison.OrdinalIgnoreCase))
                        errors.Add($"Required template fact '{fact.Id}' was cited but did not retain '{term}'.");
                }
            }

            foreach (var negation in new[] { "no", "not", "never", "without", "declined" })
            {
                if (ContainsWholeWord(fact.Text, negation) && !ContainsWholeWord(supportedText, negation))
                    errors.Add($"Required fact '{fact.Id}' lost meaningful negation '{negation}'.");
            }

            var supportedQuotations = QuotationRegex().Matches(supportedText)
                .Select(QuotationContent)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var quotation in QuotationRegex().Matches(fact.Text).Cast<Match>().Select(QuotationContent))
            {
                if (!supportedQuotations.Contains(quotation))
                    errors.Add($"Required fact '{fact.Id}' did not preserve the supplied quotation exactly.");
            }
        }

        return new CaseNoteDraftValidationResult(errors.Distinct(StringComparer.Ordinal).ToList(), used);
    }

    public static string Render(string caseManagerFullName, CaseNoteDraftPlan plan)
    {
        if (string.IsNullOrWhiteSpace(caseManagerFullName))
            throw new ArgumentException("A case-manager name is required.", nameof(caseManagerFullName));
        if (plan.Sentences is null || plan.Sentences.Count == 0 || plan.FollowUp is null)
            throw new ArgumentException("Only a validated, complete draft plan can be rendered.", nameof(plan));

        var paragraphs = new List<string>();
        foreach (var group in plan.Sentences.GroupBy(sentence => sentence.Paragraph))
            paragraphs.Add(string.Join(" ", group.Select(sentence => sentence.Text.Trim())));

        if (!paragraphs[0].StartsWith("CCM ", StringComparison.Ordinal))
            throw new ArgumentException("The first validated sentence must begin with 'CCM '.", nameof(plan));
        paragraphs[0] = $"Community Case Manager (CCM) {caseManagerFullName.Trim()} {paragraphs[0][4..]}";
        return string.Join(Environment.NewLine + Environment.NewLine, paragraphs) +
               Environment.NewLine + Environment.NewLine +
               $"Follow-up: {plan.FollowUp.Text.Trim()}";
    }

    private static void ValidateSentence(
        CaseNoteDraftSentence sentence,
        CaseNoteFactUsage requiredUsage,
        IReadOnlyDictionary<string, CaseNoteDraftFact> factById,
        string trustedTerms,
        ISet<string> used,
        ICollection<string> errors,
        bool isFollowUp)
    {
        if (string.IsNullOrWhiteSpace(sentence.Text))
        {
            errors.Add(isFollowUp
                ? "The Follow-up section is blank."
                : "The grounded draft contains a blank sentence.");
            return;
        }

        var trimmedSentence = sentence.Text.Trim();
        if (trimmedSentence.Contains('\r') || trimmedSentence.Contains('\n'))
            errors.Add($"The sentence '{Shorten(trimmedSentence)}' contains an unexpected line break.");
        if (!TerminalPunctuationRegex().IsMatch(trimmedSentence))
            errors.Add($"The sentence '{Shorten(trimmedSentence)}' is not a complete punctuated sentence.");
        var firstLetter = trimmedSentence.FirstOrDefault(char.IsLetter);
        if (firstLetter != default && !char.IsUpper(firstLetter))
            errors.Add($"The sentence '{Shorten(trimmedSentence)}' does not begin professionally.");

        if (sentence.FactIds is null || sentence.FactIds.Count == 0)
        {
            errors.Add($"The sentence '{Shorten(sentence.Text)}' does not cite a current-note fact.");
            return;
        }

        var citedFacts = new List<CaseNoteDraftFact>();
        foreach (var factId in sentence.FactIds.Distinct(StringComparer.Ordinal))
        {
            if (!factById.TryGetValue(factId, out var fact))
            {
                errors.Add($"The draft cited unknown fact id '{factId}'.");
                continue;
            }

            if (!fact.Usage.HasFlag(requiredUsage))
                errors.Add($"Fact '{factId}' is not permitted in the {(isFollowUp ? "Follow-up" : "narrative")} section.");

            used.Add(factId);
            citedFacts.Add(fact);
        }

        if (citedFacts.Count == 0)
            return;

        if (PlaceholderRegex().IsMatch(sentence.Text))
            errors.Add($"The sentence '{Shorten(sentence.Text)}' contains a placeholder.");

        var citedText = string.Join("\n", citedFacts.Select(fact => fact.Text));
        foreach (var negation in new[] { "no", "not", "never", "without", "declined" })
        {
            if (ContainsWholeWord(sentence.Text, negation) && !ContainsWholeWord(citedText, negation))
                errors.Add($"The sentence '{Shorten(sentence.Text)}' introduced unsupported negation '{negation}'.");
        }

        foreach (Match number in NumberLikeTokenRegex().Matches(sentence.Text))
        {
            if (!citedText.Contains(number.Value, StringComparison.OrdinalIgnoreCase) &&
                !trustedTerms.Contains(number.Value, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"The sentence '{Shorten(sentence.Text)}' introduced unsupported numeric value '{number.Value}'.");
            }
        }

        foreach (Match quotation in QuotationRegex().Matches(sentence.Text))
        {
            var quoted = quotation.Groups[1].Success ? quotation.Groups[1].Value : quotation.Groups[2].Value;
            if (!citedText.Contains(quoted, StringComparison.Ordinal))
                errors.Add($"The sentence '{Shorten(sentence.Text)}' introduced an unsupported quotation.");
        }

        foreach (Match capital in CapitalizedTokenRegex().Matches(sentence.Text))
        {
            if (StructuralCapitalWords.Contains(capital.Value))
                continue;
            if (citedText.Contains(capital.Value, StringComparison.OrdinalIgnoreCase) ||
                trustedTerms.Contains(capital.Value, StringComparison.OrdinalIgnoreCase))
                continue;

            errors.Add($"The sentence '{Shorten(sentence.Text)}' introduced unsupported name or term '{capital.Value}'.");
        }


        ValidateClosedVocabulary(sentence.Text, citedText, trustedTerms, errors);

        var citesFallback = citedFacts.Any(fact => fact.Id == NoFollowUpFactId);
        if (citesFallback)
        {
            if (!isFollowUp)
                errors.Add("The no-follow-up fallback may appear only in the Follow-up section.");
            if (!string.Equals(sentence.Text.Trim(), NoFollowUpText, StringComparison.Ordinal))
                errors.Add("The no-follow-up fallback must be rendered exactly and may not be expanded into an invented action.");
            if (citedFacts.Count != 1)
                errors.Add("The no-follow-up fallback cannot be combined with a current-contact fact.");
        }
    }

    private static string Shorten(string value) =>
        value.Length <= 80 ? value : value[..77] + "...";

    private static void ValidateClosedVocabulary(
        string sentence,
        string citedText,
        string trustedTerms,
        ICollection<string> errors)
    {
        var sourceWords = CoverageWordRegex().Matches(citedText + " " + trustedTerms)
            .Select(match => NormalizeWord(match.Value))
            .Where(word => word.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceStems = sourceWords.Select(Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var match in CoverageWordRegex().Matches(sentence).Cast<Match>())
        {
            var word = NormalizeWord(match.Value);
            if (StructuralVocabulary.Contains(word) ||
                sourceWords.Contains(word) || sourceStems.Contains(Stem(word)))
                continue;

            var family = SupportedWordFamilies.FirstOrDefault(items =>
                items.Contains(word, StringComparer.OrdinalIgnoreCase));
            if (family is not null && family.Any(item => sourceWords.Contains(item)))
                continue;

            errors.Add($"The sentence '{Shorten(sentence)}' introduced unsupported content word '{match.Value}'.");
        }
    }

    private static string NormalizeWord(string value) =>
        value.Trim().Trim('\'', '’', '-', '–').ToLowerInvariant();

    private static bool IsWordSupportedBy(string sourceTerm, IReadOnlySet<string> proposedWords)
    {
        var source = NormalizeWord(sourceTerm);
        if (proposedWords.Contains(source) || proposedWords.Any(word => Stem(word) == Stem(source)))
            return true;

        var family = SupportedWordFamilies.FirstOrDefault(items =>
            items.Contains(source, StringComparer.OrdinalIgnoreCase));
        return family is not null && family.Any(item => proposedWords.Contains(item));
    }

    private static bool AppearsInOrder(
        IReadOnlyList<string> sourceTerms,
        IReadOnlyList<string> proposedWords)
    {
        var proposedIndex = 0;
        foreach (var term in sourceTerms)
        {
            var matched = false;
            while (proposedIndex < proposedWords.Count)
            {
                var candidate = proposedWords[proposedIndex++];
                if (IsWordSupportedBy(term, new HashSet<string>([candidate], StringComparer.OrdinalIgnoreCase)))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
                return false;
        }

        return true;
    }

    private static string QuotationContent(Match quotation) =>
        quotation.Groups[1].Success ? quotation.Groups[1].Value : quotation.Groups[2].Value;

    private static string Stem(string value)
    {
        var word = value;
        if (word.Length > 5 && word.EndsWith("ies", StringComparison.Ordinal))
            return word[..^3] + "y";
        if (word.Length > 6 && word.EndsWith("ing", StringComparison.Ordinal))
            return word[..^3];
        if (word.Length > 5 && word.EndsWith("ed", StringComparison.Ordinal))
            return word[..^2];
        if (word.Length > 4 && word.EndsWith('s'))
            return word[..^1];
        return word;
    }

    private static bool ContainsWholeWord(string text, string word) =>
        Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(word)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase);

    [GeneratedRegex(@"(?<!\w)(?:\d{1,2}[:/]\d{1,4}(?:[:/]\d{1,4})?|\d+(?:\.\d+)?)(?!\w)")]
    private static partial Regex NumberLikeTokenRegex();

    [GeneratedRegex(@"\[(?:insert|add|enter|unknown|missing)[^\]]*\]|<[^>]+>", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex("[\"“]([^\"”]+)[\"”]|'([^']+)'")]
    private static partial Regex QuotationRegex();

    [GeneratedRegex(@"\b(?:[A-Z]{2,}|[A-Z][a-z]{2,})\b")]
    private static partial Regex CapitalizedTokenRegex();

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’-]*")]
    private static partial Regex CoverageWordRegex();

    [GeneratedRegex(@"[.!?][\""'”’)]?$", RegexOptions.CultureInvariant)]
    private static partial Regex TerminalPunctuationRegex();
}
