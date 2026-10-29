namespace NumberForge.Api.Services;
using NumberForge.Core;
using System.Collections.Concurrent;

public class GameSessionStore
{
    private ConcurrentDictionary<Guid, GameSession> _sessions = new ConcurrentDictionary<Guid, GameSession>();
    public Guid Add(GameSession session)
    {
        Guid sessionId = Guid.NewGuid();
        _sessions[sessionId] = session;
        return sessionId;
    }

    public GameSession? Get(Guid sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }
}