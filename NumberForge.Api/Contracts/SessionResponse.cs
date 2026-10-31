namespace NumberForge.Api.Contracts;

public class SessionResponse
{
    public SessionResponse(
        Guid sessionId,
        int currentNumber,
        int targetNumber,
        int movesUsed,
        int maxMoves,
        IReadOnlyList<OperationResponse> availableOperations,
        IReadOnlyList<MoveResponse> history,
        CharmResponse? charm = null,
        string? puzzleId = null,
        string? puzzleName = null,
        double? elapsedSeconds = null,
        bool? isPerfect = null,
        string? collectionKey = null,
        int? totalRoutes = null,
        string? routeId = null,
        int revision = 0,
        DateTimeOffset? playUntilUtc = null,
        DateTimeOffset? expiresAtUtc = null,
        string status = "active")
    {
        SessionId = sessionId;
        CurrentNumber = currentNumber;
        TargetNumber = targetNumber;
        MovesUsed = movesUsed;
        MaxMoves = maxMoves;
        AvailableOperations = new List<OperationResponse>(availableOperations).AsReadOnly();

        History = new List<MoveResponse>(history).AsReadOnly();

        // The get-only CharmResponse preserves this snapshot's charm state.
        Charm = charm;
        PuzzleId = puzzleId;
        PuzzleName = puzzleName;
        ElapsedSeconds = elapsedSeconds;
        IsPerfect = isPerfect;
        CollectionKey = collectionKey;
        TotalRoutes = totalRoutes;
        RouteId = routeId;
        Revision = revision;
        PlayUntilUtc = playUntilUtc;
        ExpiresAtUtc = expiresAtUtc;
        Status = status;
    }

    public Guid SessionId { get; }
    // Clients can submit this revision with a move to reject stale tabs safely.
    public int Revision { get; }
    public DateTimeOffset? PlayUntilUtc { get; }
    public DateTimeOffset? ExpiresAtUtc { get; }
    public string Status { get; }

    // The browser can identify and replay a restored game without guessing from its numbers.
    public string? PuzzleId { get; }
    public string? PuzzleName { get; }

    // Hidden while playing; once the game ends this is a fixed duration in seconds.
    public double? ElapsedSeconds { get; }

    // Only wins disclose a grade. The exact minimum card count stays on the server.
    public bool? IsPerfect { get; }

    public string? CollectionKey { get; }
    public int? TotalRoutes { get; }

    // A completed winning route gets a stable identity; active and lost games return null.
    public string? RouteId { get; }
    public int CurrentNumber { get; }
    public int TargetNumber { get; }
    public int MovesUsed { get; }
    public int MaxMoves { get; }
    public IReadOnlyList<OperationResponse> AvailableOperations { get; }
    public IReadOnlyList<MoveResponse> History { get; }

    // Null means this session has no charm.
    public CharmResponse? Charm { get; }
}
