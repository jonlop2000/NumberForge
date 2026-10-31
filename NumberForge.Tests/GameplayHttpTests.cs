using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NumberForge.Api.Contracts;

namespace NumberForge.Tests;

// These tests exercise the deployed controller/authentication/model-binding path, not just controller methods.
public sealed class GameplayHttpTests
{
    [Fact]
    public async Task Catalog_IsBoundedAndConcealsSolutions_AndUnlimitedGenerationIsGone()
    {
        await using var factory = new GameplayFactory();
        using var player = factory.Player();
        var puzzles = await player.GetFromJsonAsync<PuzzleResponse[]>("/api/puzzles");
        Assert.Equal(7, puzzles!.Length);
        Assert.All(puzzles, puzzle => Assert.Null(puzzle.TotalRoutes));
        var daily = await player.GetFromJsonAsync<DailyPuzzleSetResponse>("/api/puzzles/daily");
        Assert.Equal(new[] { 3, 4, 5 }, daily!.Puzzles.Select(p => p.MaxMoves));
        Assert.Equal(HttpStatusCode.Gone, (await player.PostAsync("/api/puzzles/generated", null)).StatusCode);
        Assert.Equal(7, (await player.GetFromJsonAsync<PuzzleResponse[]>("/api/puzzles"))!.Length);
    }

    [Fact]
    public async Task SeparateCookieJars_CannotReadOrChangeAnotherPlayersAttempt()
    {
        await using var factory = new GameplayFactory();
        using var owner = factory.Player();
        using var stranger = factory.Player();
        var created = await owner.PostAsync("/api/sessions?puzzleId=demo", null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var session = (await created.Content.ReadFromJsonAsync<SessionResponse>())!;
        string path = $"/api/sessions/{session.SessionId}";
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(path + "/moves", new { operationId = 1 })).StatusCode);

        var unchanged = await owner.GetFromJsonAsync<SessionResponse>(path);
        Assert.Equal(0, unchanged!.MovesUsed);
        Assert.Equal(3, unchanged.CurrentNumber);
        var moved = await owner.PostAsJsonAsync(path + "/moves", new { operationId = 1 });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(7, (await moved.Content.ReadFromJsonAsync<SessionResponse>())!.CurrentNumber);
    }

    [Fact]
    public async Task ClientCannotSubmitGameStateOrReuseACard()
    {
        await using var factory = new GameplayFactory();
        using var player = factory.Player();
        var session = (await (await player.PostAsync("/api/sessions?puzzleId=demo", null)).Content.ReadFromJsonAsync<SessionResponse>())!;
        string path = $"/api/sessions/{session.SessionId}";
        var forged = await player.PostAsJsonAsync(path + "/moves", new { operationId = 1, currentNumber = 14, isWon = true });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal(0, (await player.GetFromJsonAsync<SessionResponse>(path))!.MovesUsed);
        Assert.Equal(HttpStatusCode.OK, (await player.PostAsJsonAsync(path + "/moves", new { operationId = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await player.PostAsJsonAsync(path + "/moves", new { operationId = 1 })).StatusCode);
        Assert.Equal(1, (await player.GetFromJsonAsync<SessionResponse>(path))!.MovesUsed);
    }

    [Fact]
    public async Task WinningRoute_UnlocksOnlyItsOwnersProgress_AndFreezesTheHiddenTimer()
    {
        await using var factory = new GameplayFactory();
        using var owner = factory.Player();
        using var stranger = factory.Player();
        var session = (await (await owner.PostAsync("/api/sessions?puzzleId=demo", null)).Content.ReadFromJsonAsync<SessionResponse>())!;
        string path = $"/api/sessions/{session.SessionId}";
        Assert.Null(session.ElapsedSeconds);
        Assert.Null(session.TotalRoutes);
        factory.Clock.Advance(TimeSpan.FromSeconds(8));
        await owner.PostAsJsonAsync(path + "/moves", new { operationId = 1 });
        factory.Clock.Advance(TimeSpan.FromSeconds(4));
        var won = (await (await owner.PostAsJsonAsync(path + "/moves", new { operationId = 2 })).Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal("won", won.Status);
        Assert.Equal(14, won.CurrentNumber);
        Assert.Equal(12, won.ElapsedSeconds);
        Assert.NotNull(won.RouteId);
        Assert.True(won.TotalRoutes > 0);

        factory.Clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(won.ElapsedSeconds, (await owner.GetFromJsonAsync<SessionResponse>(path))!.ElapsedSeconds);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(path + "/moves", new { operationId = 3 })).StatusCode);
        var ownerPuzzle = (await owner.GetFromJsonAsync<PuzzleResponse[]>("/api/puzzles"))!.Single(p => p.Id == "demo");
        var strangerPuzzle = (await stranger.GetFromJsonAsync<PuzzleResponse[]>("/api/puzzles"))!.Single(p => p.Id == "demo");
        Assert.NotNull(ownerPuzzle.TotalRoutes);
        Assert.Null(strangerPuzzle.TotalRoutes);
    }

    [Fact]
    public async Task MoveRetry_WithSameRequestId_DoesNotDoubleApply()
    {
        await using var factory = new GameplayFactory();
        using var player = factory.Player();
        var session = (await (await player.PostAsync("/api/sessions?puzzleId=demo", null)).Content.ReadFromJsonAsync<SessionResponse>())!;
        string path = $"/api/sessions/{session.SessionId}/moves";
        var request = new { operationId = 1, expectedRevision = 0, requestId = Guid.NewGuid() };
        var first = await player.PostAsJsonAsync(path, request);
        var retry = await player.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var state = (await retry.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal(1, state.MovesUsed);
        Assert.Equal(7, state.CurrentNumber);
        Assert.Equal(HttpStatusCode.Conflict,
            (await player.PostAsJsonAsync(path, new { operationId = 2, expectedRevision = 0, requestId = Guid.NewGuid() })).StatusCode);
    }

    private sealed class GameplayFactory : WebApplicationFactory<Program>
    {
        public GameplayClock Clock { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NumberForge"] = string.Empty
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            });
        }
        public HttpClient Player()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("http://localhost:5100"), HandleCookies = true, AllowAutoRedirect = false
            });
            client.DefaultRequestHeaders.Add("Origin", "http://localhost:5100");
            return client;
        }
    }

    private sealed class GameplayClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) { _now += duration; _timestamp += duration.Ticks; }
    }
}
