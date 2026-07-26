using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Axon.Control.Security;

public sealed record OperatorSession(string UserId, string AccessToken, DateTimeOffset ExpiresAt);

public sealed class OperatorSessions
{
    public const string CookieName = "axon_operator";
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
    private readonly ConcurrentDictionary<string, OperatorSession> _sessions = new(StringComparer.Ordinal);

    public string Create(string userId, string accessToken)
    {
        var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        _sessions[key] = new(userId, accessToken, DateTimeOffset.UtcNow.Add(Lifetime));
        return key;
    }

    public bool TryGet(string? key, out OperatorSession session)
    {
        session = default!;
        if (string.IsNullOrWhiteSpace(key) || !_sessions.TryGetValue(key, out var found))
        {
            return false;
        }
        if (found.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(key, out _);
            return false;
        }
        session = found;
        return true;
    }

    public void Remove(string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            _sessions.TryRemove(key, out _);
        }
    }
}
