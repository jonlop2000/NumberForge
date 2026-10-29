namespace NumberForge.Api.Services;
using NumberForge.Core;
using System.Collections.Concurrent;
using System.Threading;

public class GameSessionStore
{
    // The dictionary protects adding and finding entries across simultaneous requests.
    // Each entry pairs a game with a separate lock for changes to that game's state.
    private readonly ConcurrentDictionary<Guid, StoredSession> _sessions = new ConcurrentDictionary<Guid, StoredSession>();

    public Guid Add(GameSession session)
    {
        Guid sessionId = Guid.NewGuid();
        _sessions[sessionId] = new StoredSession(session);
        return sessionId;
    }

    public GameSession? Get(Guid sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var entry) ? entry.Session : null;
    }

    // Controllers call this after adding a session or confirming that it exists.
    // The same session ID always supplies the same lock to every controller instance.
    internal Lock GetLock(Guid sessionId)
    {
        return _sessions[sessionId].SyncRoot;
    }

    private sealed class StoredSession
    {
        public StoredSession(GameSession session)
        {
            Session = session;
        }

        public GameSession Session { get; }

        // A dedicated lock keeps unrelated games free to process their own moves.
        public Lock SyncRoot { get; } = new Lock();
    }
}
