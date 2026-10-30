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
        IReadOnlyList<MoveResponse> history)
    {
        SessionId = sessionId;
        CurrentNumber = currentNumber;
        TargetNumber = targetNumber;
        MovesUsed = movesUsed;
        MaxMoves = maxMoves;
        AvailableOperations = new List<OperationResponse>(availableOperations).AsReadOnly();

        // Copy the list so later changes cannot rewrite this response's history snapshot.
        History = new List<MoveResponse>(history).AsReadOnly();
    }

    public Guid SessionId { get; }
    public int CurrentNumber { get; }
    public int TargetNumber { get; }
    public int MovesUsed { get; }
    public int MaxMoves { get; }
    public IReadOnlyList<OperationResponse> AvailableOperations { get; }
    public IReadOnlyList<MoveResponse> History { get; }
}
