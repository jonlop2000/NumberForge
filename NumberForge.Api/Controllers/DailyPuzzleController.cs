using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NumberForge.Api.Contracts;
using NumberForge.Api.Security;
using NumberForge.Api.Services;

namespace NumberForge.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/puzzles/daily")]
public sealed class DailyPuzzleController(DailyPuzzleService dailyPuzzles, PuzzleResponseMapper mapper) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetToday(CancellationToken cancellationToken = default)
    {
        try
        {
            var set = dailyPuzzles.GetToday(cancellationToken);
            var puzzles = await Task.WhenAll(set.Puzzles.Select(p => mapper.MapAsync(p, PlayerIdentity.GetRequiredId(User), cancellationToken)));
            return Ok(new DailyPuzzleSetResponse(set.Date, set.AvailableFromUtc, set.ExpiresAtUtc, puzzles));
        }
        catch (InvalidOperationException)
        {
            return Problem(statusCode: 503, title: "Today's puzzles are temporarily unavailable", detail: "Please try again shortly.");
        }
    }
}
