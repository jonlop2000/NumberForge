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

        var response = ToResponse(sessionId, session);

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

        return Ok(ToResponse(sessionId, session));
    }

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

        return new SessionResponse(
            sessionId,
            session.CurrentNumber,
            session.Puzzle.TargetNumber,
            session.MovesUsed,
            session.MaxMoves,
            operationResponses);
    }
}
