namespace NumberForge.Api.Contracts;

// This DTO defines the JSON data needed to display a puzzle and its starting hand.
public class PuzzleResponse
{
    public PuzzleResponse(
        string id,
        string name,
        int startNumber,
        int targetNumber,
        int maxMoves,
        IReadOnlyList<OperationResponse> operations,
        string collectionKey,
        int? totalRoutes)
    {
        Id = id;
        Name = name;
        StartNumber = startNumber;
        TargetNumber = targetNumber;
        MaxMoves = maxMoves;
        CollectionKey = collectionKey;
        TotalRoutes = totalRoutes;

        // Keep a read-only snapshot so editing the supplied list cannot change this response.
        Operations = new List<OperationResponse>(operations).AsReadOnly();
    }

    public string Id { get; }
    public string Name { get; }
    public string CollectionKey { get; }
    public int? TotalRoutes { get; }
    public int StartNumber { get; }
    public int TargetNumber { get; }
    public int MaxMoves { get; }
    public IReadOnlyList<OperationResponse> Operations { get; }
}
