using Microsoft.AspNetCore.Mvc;
using NumberForge.Api.Contracts;
using NumberForge.Core;

namespace NumberForge.Api.Controllers
{
    [ApiController]
    [Route("api/puzzles")]
    public class PuzzleController : ControllerBase
    {
        [HttpGet("demo")]
        public IActionResult GetDemo()
        {
            // Build the demo using the same Core types used by the console game.
            var puzzle = new Puzzle(3, 12);
            int maxMoves = 3;
            List<IMathOperation> operations = new List<IMathOperation>
            {
                new MathOperation(5),
                new MultiplyOperation(2),
                new MathOperation(-4)
            };

            // Give each starting card an ID and read its description from the Core operation.
            var operationResponses = new List<OperationResponse>();
            for (int i = 0; i < operations.Count; i++)
            {
                operationResponses.Add(new OperationResponse(i + 1, operations[i].Description));
            }

            // The API chooses the response format; the puzzle supplies its actual numbers.
            var response = new PuzzleResponse(
                puzzle.StartNumber,
                puzzle.TargetNumber,
                maxMoves,
                operationResponses);

            // Ok returns HTTP 200, and ASP.NET serializes the DTO's properties as JSON.
            return Ok(response);
        }
    }
}
