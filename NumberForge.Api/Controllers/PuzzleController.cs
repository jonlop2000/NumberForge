using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NumberForge.Api.Security;
using NumberForge.Api.Services;

namespace NumberForge.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/puzzles")]
public sealed class PuzzleController(PuzzleCatalog catalog, PuzzleResponseMapper mapper) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default) =>
        Ok(await Task.WhenAll(catalog.All.Select(p => mapper.MapAsync(p, PlayerIdentity.GetRequiredId(User), cancellationToken))));

    [HttpGet("{puzzleId}")]
    public async Task<IActionResult> GetById(string puzzleId, CancellationToken cancellationToken = default)
    {
        var definition = catalog.Get(puzzleId);
        return definition is null ? NotFound() : Ok(await mapper.MapAsync(definition, PlayerIdentity.GetRequiredId(User), cancellationToken));
    }

    [HttpPost("generated")]
    public IActionResult CreateGenerated() => Problem(statusCode: 410, title: "Daily challenges have replaced random generation",
        detail: "Choose from today's three challenges or the practice puzzles.");
}
