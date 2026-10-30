using Microsoft.AspNetCore.Mvc;
using NumberForge.Api.Contracts;
using NumberForge.Api.Services;
using NumberForge.Core;

namespace NumberForge.Api.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionController : ControllerBase
{
    private readonly GameSessionStore _sessionStore;

    public SessionController(GameSessionStore sessionStore)
    {
        _sessionStore = sessionStore;
    }

    [HttpPost]
    public IActionResult Create()
    {
        var puzzle = new Puzzle(3, 12);
        List<IMathOperation> startingOperations = new List<IMathOperation>
        {
            new MathOperation(5),
            new MultiplyOperation(2),
            new MathOperation(-4)
        };
        var session = new GameSession(puzzle, 3, startingOperations);

        Guid sessionId = _sessionStore.Add(session);

        // Capture the response under the same per-game lock used by GET and move requests.
        SessionResponse response;
        lock (_sessionStore.GetLock(sessionId))
        {
            response = ToResponse(sessionId, session);
        }

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpGet("{sessionId:guid}")]
    public IActionResult GetById(Guid sessionId)
    {
        var session = _sessionStore.Get(sessionId);

        if (session is null)
        {
            return NotFound();
        }

        // Wait for any move in progress so the cards and counters describe one consistent state.
        lock (_sessionStore.GetLock(sessionId))
        {
            return Ok(ToResponse(sessionId, session));
        }
    }

    // Read the session ID from the URL and the selected card ID from the JSON request body.
    [HttpPost("{sessionId:guid}/moves")]
    public IActionResult ApplyMove(Guid sessionId, [FromBody] ApplyMoveRequest request)
    {
        var session = _sessionStore.Get(sessionId);
        if (session is null)
        {
            return NotFound();
        }

        // Keep validation, applying the move, and taking its snapshot together.
        // A second request for the same game waits here until the first finishes.
        lock (_sessionStore.GetLock(sessionId))
        {
            if (request.OperationId < 1 || request.OperationId > session.StartingOperations.Count)
            {
                // Problem returns a standard JSON error description with the chosen HTTP status.
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid operation ID",
                    detail: "Choose a card from this game's starting hand.");
            }

            // IDs refer to original positions, even after other cards have been removed.
            var operation = session.StartingOperations[request.OperationId - 1];

            // Core owns the arithmetic, card consumption, move limit, and target checks.
            bool accepted = session.TryApplyOperation(operation);
            if (!accepted)
            {
                // The ID exists, but using it conflicts with the game's current state.
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Move rejected",
                    detail: "This card has already been used or the game has ended.");
            }

            // The DTO copies the updated state before another request can change the game.
            return Ok(ToResponse(sessionId, session));
        }
    }

    // Call while holding this game's lock so all response fields come from the same moment.
    private static SessionResponse ToResponse(Guid sessionId, GameSession session)
    {
        var operationResponses = new List<OperationResponse>();

        for (int i = 0; i < session.StartingOperations.Count; i++)
        {
            var operation = session.StartingOperations[i];

            if (session.AvailableOperations.Contains(operation))
            {
                operationResponses.Add(new OperationResponse(i + 1, operation.Description));
            }
        }

        // Translate Core's move records into API contracts, preserving their original order.
        var moveResponses = new List<MoveResponse>();
        foreach (var move in session.History)
        {
            moveResponses.Add(new MoveResponse(
                move.BeforeNumber,
                move.OperationDescription,
                move.AfterNumber));
        }

        return new SessionResponse(
            sessionId,
            session.CurrentNumber,
            session.Puzzle.TargetNumber,
            session.MovesUsed,
            session.MaxMoves,
            operationResponses,
            moveResponses);
    }
}
