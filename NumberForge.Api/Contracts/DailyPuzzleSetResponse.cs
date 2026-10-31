namespace NumberForge.Api.Contracts;

// The dates describe the server's daily boundary so clients do not guess from a local clock or time zone.
public sealed class DailyPuzzleSetResponse
{
    public DailyPuzzleSetResponse(DateOnly date, DateTimeOffset availableFromUtc, DateTimeOffset expiresAtUtc,
        IReadOnlyList<PuzzleResponse> puzzles)
    {
        Date = date;
        AvailableFromUtc = availableFromUtc;
        ExpiresAtUtc = expiresAtUtc;
        Puzzles = Array.AsReadOnly(puzzles.ToArray());
    }

    public DateOnly Date { get; }
    public DateTimeOffset AvailableFromUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public IReadOnlyList<PuzzleResponse> Puzzles { get; }
}
