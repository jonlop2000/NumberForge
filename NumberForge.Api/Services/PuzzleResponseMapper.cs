using NumberForge.Api.Contracts;
using NumberForge.Core;

namespace NumberForge.Api.Services;

public sealed class PuzzleResponseMapper(GameSessionStore store)
{
    public async Task<PuzzleResponse> MapAsync(PuzzleDefinition definition, string playerId, CancellationToken cancellationToken = default)
    {
        string key = RouteIdentity.GetCollectionKey(definition);
        var discoveries = await store.GetRoutesAsync(playerId, key, cancellationToken);
        // The first win unlocks route hunting. Neither the minimum nor predicted results are sent.
        return new PuzzleResponse(definition.Id, definition.Name, definition.Puzzle.StartNumber, definition.Puzzle.TargetNumber,
            definition.MaxMoves, definition.Operations.Select((op, i) => new OperationResponse(i + 1, op.Description)).ToArray(),
            key, discoveries.Count > 0 ? definition.TotalRoutes : null);
    }
}
