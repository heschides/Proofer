namespace Sati.Services.LocalAi
{
    public sealed class LocalAiOptions
    {
        public const string SectionName = "LocalAi";

        public bool Enabled { get; set; }
        public string ModelAlias { get; set; } = "phi-4-mini";
        public int MaxInputWords { get; set; } = 500;
        public int MaxOutputTokens { get; set; } = 900;
        public int RecentContextNoteCount { get; set; } = 10;
        public int MatchingContextNoteCount { get; set; } = 5;
        public int MaxContextCharacters { get; set; } = 24_000;
        public int MaxContextNoteCharacters { get; set; } = 1_200;
        public int MaxAssessmentContextCharacters { get; set; } = 8_000;
        public string RulesFile { get; set; } = "AI_CASE_NOTE_RULES.md";
        public string? DataDirectory { get; set; }
    }
}
