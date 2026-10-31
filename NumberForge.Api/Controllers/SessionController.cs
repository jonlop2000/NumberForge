using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NumberForge.Api.Contracts;
using NumberForge.Api.Security;
using NumberForge.Api.Services;
using NumberForge.Core;

namespace NumberForge.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sessions")]
public sealed class SessionController(GameSessionStore sessionStore, PuzzleCatalog catalog, TimeProvider? clock = null) : ControllerBase
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private string PlayerId => PlayerIdentity.GetRequiredId(User);

    [HttpPost]
    public async Task<IActionResult> Create([FromQuery] string puzzleId = PuzzleCatalog.DefaultPuzzleId, CancellationToken cancellationToken = default)
    {
        var definition = catalog.Get(puzzleId);
        if (definition is null) return Problem(statusCode: 404, title: "Puzzle not found", detail: "Choose a puzzle from today's set or practice.");
        DateTimeOffset? playUntil = DailyPuzzleService.TryParsePuzzleId(definition.Id, out var date, out _)
            ? new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
        if (playUntil <= _clock.GetUtcNow()) return Problem(statusCode: 410, title: "This daily set has ended");
        Guid? id = await sessionStore.AddAsync(new GameSession(definition), PlayerId, playUntil, cancellationToken);
        if (id is null)
        {
            Response.Headers.RetryAfter = "60";
            return Problem(statusCode: 429, title: "Attempt limit reached", detail: "Continue an existing attempt or try again later.");
        }
        var state = await sessionStore.GetAsync(id.Value, PlayerId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { sessionId = id.Value }, await ToResponse(state!, cancellationToken));
    }

    [HttpGet("{sessionId:guid}")]
    public async Task<IActionResult> GetById(Guid sessionId, CancellationToken cancellationToken = default)
    {
        // A missing game and another player's game deliberately have the same response.
        var state = await sessionStore.GetAsync(sessionId, PlayerId, cancellationToken);
        return state is null ? NotFound() : Ok(await ToResponse(state, cancellationToken));
    }

    [HttpPost("{sessionId:guid}/moves")]
    public async Task<IActionResult> ApplyMove(Guid sessionId, [FromBody] ApplyMoveRequest request, CancellationToken cancellationToken = default)
    {
        var result = await sessionStore.ApplyAsync(sessionId, PlayerId, request.OperationId, request.ExpectedRevision, request.RequestId, cancellationToken);
        return result.Status switch
        {
            MoveStatus.Accepted => Ok(await ToResponse(result.State!, cancellationToken)),
            MoveStatus.NotFound => NotFound(),
            MoveStatus.Expired => Problem(statusCode: 410, title: "This daily set has ended", detail: "Choose a puzzle from today's set."),
            MoveStatus.InvalidOperation => Problem(statusCode: 400, title: "Invalid operation ID", detail: "Choose a card from this game's starting hand."),
            _ => Problem(statusCode: 409, title: "Move rejected", detail: "Refresh this attempt. The card, revision, or game state has changed.")
        };
    }

    private async Task<SessionResponse> ToResponse(SessionState state, CancellationToken cancellationToken)
    {
        var game = state.Game;
        var definition = game.Definition;
        string? key = definition is null ? null : RouteIdentity.GetCollectionKey(definition);
        var discoveries = key is null ? null : await sessionStore.GetRoutesAsync(PlayerId, key, cancellationToken);
        bool won = game.Puzzle.IsTargetReached(game.CurrentNumber);
        bool expired = state.Document.PlayUntilUtc <= _clock.GetUtcNow();
        string status = won ? "won" : game.IsFinished ? "lost" : expired ? "expired" : "active";
        return new SessionResponse(state.Document.Id, game.CurrentNumber, game.Puzzle.TargetNumber, game.MovesUsed, game.MaxMoves,
            game.StartingOperations.Select((op, index) => (op, index)).Where(c => game.AvailableOperations.Contains(c.op))
                .Select(c => new OperationResponse(c.index + 1, c.op.Description)).ToArray(),
            game.History.Select(m => new MoveResponse(m.BeforeNumber, m.OperationDescription, m.AfterNumber)).ToArray(),
            game.Charm == CharmType.Echo ? new CharmResponse("Echo", "Your first positive addition activates twice.", game.EchoUsed) : null,
            definition?.Id, definition?.Name, state.ElapsedSeconds, game.IsPerfect, key,
            discoveries?.Count > 0 ? definition!.TotalRoutes : null, RouteIdentity.GetWinningRouteId(game),
            state.Document.Revision, state.Document.PlayUntilUtc, state.Document.ExpiresAtUtc, status);
    }
}
