using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NumberForge.Api.Security;

namespace NumberForge.Tests;

internal static class TestPlayer
{
    internal const string Id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string OtherId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    internal static T ForPlayer<T>(this T controller, string playerId = Id) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = PlayerIdentity.CreatePrincipal(playerId) }
        };
        return controller;
    }
}
