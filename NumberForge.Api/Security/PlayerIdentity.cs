using System.Security.Claims;

namespace NumberForge.Api.Security;

public static class PlayerIdentity
{
    public const string Scheme = "NumberForgePlayer";
    public const string DevelopmentCookieName = "numberforge.player";
    public const string ProductionCookieName = "__Host-numberforge.player";

    public static string GetRequiredId(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Identity?.IsAuthenticated != true || !IsValidId(id))
        {
            throw new InvalidOperationException("An authenticated player identity is required.");
        }
        return id!;
    }

    public static ClaimsPrincipal CreatePrincipal(string id)
    {
        if (!IsValidId(id))
        {
            throw new ArgumentException("Player IDs must contain 64 lowercase hexadecimal characters.", nameof(id));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, id) }, Scheme));
    }

    private static bool IsValidId(string? id)
    {
        return id is { Length: 64 } && id.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
