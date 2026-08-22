namespace Sati.Services.LocalAi
{
    public sealed class LocalAiOptions
    {
        public const string SectionName = "LocalAi";

        public bool Enabled { get; set; }
        public string ModelAlias { get; set; } = "phi-4-mini";
        public int MaxInputWords { get; set; } = 500;
        public int MaxOutputTokens { get; set; } = 900;
        public string RulesFile { get; set; } = "AI_CASE_NOTE_RULES.md";
        public string? DataDirectory { get; set; }
    }
}
