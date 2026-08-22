namespace Sati.Services.LocalAi
{
    /// <summary>
    /// Tracks which consumer's fact packet the shared in-process model most recently received.
    /// Sati does not trust the native local-inference runtime to discard conversational
    /// state between chat-completion calls on its own, so this is the hard boundary:
    /// whenever the target consumer changes, the caller must force a clean model reload
    /// before the next generation, even though a fresh chat-completion request is already
    /// sent every time.
    /// </summary>
    internal sealed class ConsumerSessionBoundary
    {
        private int? _lastPersonId;

        public bool RequiresReset(int personId)
        {
            if (personId <= 0)
                throw new ArgumentOutOfRangeException(nameof(personId));

            return _lastPersonId.HasValue && _lastPersonId.Value != personId;
        }

        public void Record(int personId) => _lastPersonId = personId;

        public void Invalidate() => _lastPersonId = null;
    }
}
