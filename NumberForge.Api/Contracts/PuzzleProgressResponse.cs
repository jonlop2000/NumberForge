namespace NumberForge.Api.Contracts;

// Only routes verified by the game server count. An empty collection does not reveal the solution count.
public sealed record PuzzleProgressResponse(string PuzzleId, string CollectionKey, bool HasSolved,
    int? TotalRoutes, int FoundRoutes, int? BestMoves, IReadOnlyList<string> RouteIds);
