using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NumberForge.Core;

namespace NumberForge.Api.Services;

public static class RouteIdentity
{
    public static string GetCollectionKey(PuzzleDefinition definition)
    {
        // Changed rules or cards create a new collection. Renaming a puzzle keeps its discoveries.
        var content = JsonSerializer.Serialize(new
        {
            RulesVersion = 1,
            definition.Id,
            definition.Puzzle.StartNumber,
            definition.Puzzle.TargetNumber,
            definition.MaxMoves,
            definition.Charm,
            Operations = definition.Operations.Select(card => new
            {
                Type = card.GetType().FullName,
                card.Description
            })
        });
        return Hash(content);
    }

    public static string? GetWinningRouteId(GameSession session)
    {
        if (session.Definition is null || !session.Puzzle.IsTargetReached(session.CurrentNumber))
        {
            return null;
        }

        // Only accepted winning history contributes. Time, session ID, and grade do not change a route.
        string orderedCards = string.Join("-", session.History.Select(move => move.OperationId));
        return Hash($"{GetCollectionKey(session.Definition)}:{orderedCards}");
    }

    private static string Hash(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }
}
