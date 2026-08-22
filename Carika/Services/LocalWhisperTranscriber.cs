using System.Text;
using Whisper.net;

namespace Carika.Services;

internal sealed class LocalWhisperTranscriber
{
    private readonly string? _modelPath = Environment.GetEnvironmentVariable("CARIKA_WHISPER_MODEL");

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_modelPath) && File.Exists(_modelPath);

    public async Task<string> TranscribeAsync(string wavePath, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Set CARIKA_WHISPER_MODEL to a locally provisioned GGML Whisper model. Carika will not download a model or use cloud transcription.");
        await using var audio = File.OpenRead(wavePath);
        using var factory = WhisperFactory.FromPath(_modelPath!);
        using var processor = factory.CreateBuilder().WithLanguage("en").Build();
        var text = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(audio, ct)) text.Append(segment.Text);
        return text.ToString().Trim();
    }
}
