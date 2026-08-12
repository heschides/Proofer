namespace Sati.Api.Security;

public sealed class LoginAttemptGuard
{
    private const int AttemptLimit = 12;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly Dictionary<string, AttemptWindow> _attempts =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryAcquire(string username, out TimeSpan retryAfter)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            RemoveExpired(now);

            if (!_attempts.TryGetValue(username, out var state))
            {
                state = new AttemptWindow(now);
                _attempts[username] = state;
            }

            if (state.Attempts >= AttemptLimit)
            {
                retryAfter = Window - (now - state.StartedAt);
                if (retryAfter <= TimeSpan.Zero)
                    retryAfter = TimeSpan.FromSeconds(1);
                return false;
            }

            state.Attempts++;
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    public void Reset(string username)
    {
        lock (_gate)
            _attempts.Remove(username);
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var username in _attempts
                     .Where(pair => now - pair.Value.StartedAt >= Window)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _attempts.Remove(username);
        }
    }

    private sealed class AttemptWindow(DateTimeOffset startedAt)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;
        public int Attempts { get; set; }
    }
}
