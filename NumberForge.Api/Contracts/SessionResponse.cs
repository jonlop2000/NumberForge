namespace NumberForge.Api.Contracts;

public class SessionResponse
{
    public SessionResponse(
        Guid sessionId,
        int currentNumber,
        int targetNumber,
        int movesUsed,
        int maxMoves,
        IReadOnlyList<OperationResponse> availableOperations)
    {
        SessionId = sessionId;
        CurrentNumber = currentNumber;
        TargetNumber = targetNumber;
        MovesUsed = movesUsed;
        MaxMoves = maxMoves;
        AvailableOperations = new List<OperationResponse>(availableOperations).AsReadOnly();
    }

    public Guid SessionId { get; }
    public int CurrentNumber { get; }
    public int TargetNumber { get; }
    public int MovesUsed { get; }
    public int MaxMoves { get; }
    public IReadOnlyList<OperationResponse> AvailableOperations { get; }
}
