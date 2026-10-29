namespace NumberForge.Api.Contracts;

// This DTO defines the JSON data needed to display a puzzle and its starting hand.
public class PuzzleResponse
{
    public PuzzleResponse(
        int startNumber,
        int targetNumber,
        int maxMoves,
        IReadOnlyList<OperationResponse> operations)
    {
        StartNumber = startNumber;
        TargetNumber = targetNumber;
        MaxMoves = maxMoves;

        // Keep a read-only snapshot so editing the supplied list cannot change this response.
        Operations = new List<OperationResponse>(operations).AsReadOnly();
    }

    public int StartNumber { get; }
    public int TargetNumber { get; }
    public int MaxMoves { get; }
    public IReadOnlyList<OperationResponse> Operations { get; }
}
