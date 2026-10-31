using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NumberForge.Api.Contracts;
using NumberForge.Api.Security;
using NumberForge.Api.Services;

namespace NumberForge.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/puzzles/{puzzleId}/progress")]
public sealed class ProgressController(PuzzleCatalog catalog, GameSessionStore store) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string puzzleId, CancellationToken cancellationToken = default)
    {
        var definition = catalog.Get(puzzleId);
        if (definition is null) return NotFound();
        string key = RouteIdentity.GetCollectionKey(definition);
        var routes = await store.GetRoutesAsync(PlayerIdentity.GetRequiredId(User), key, cancellationToken);
        return Ok(new PuzzleProgressResponse(puzzleId, key, routes.Count > 0, routes.Count > 0 ? definition.TotalRoutes : null,
            routes.Count, routes.Count > 0 ? routes.Min(r => r.MoveCount) : null, Array.AsReadOnly(routes.Select(r => r.RouteId).ToArray())));
    }
}
