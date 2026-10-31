namespace NumberForge.Core;

// One day's fixed challenge setup. Player attempts and discoveries belong to separate session state.
public sealed class DailyPuzzleSet
{
    public DailyPuzzleSet(DateOnly date, IReadOnlyList<PuzzleDefinition> puzzles)
    {
        ArgumentNullException.ThrowIfNull(puzzles);
        var copy = puzzles.ToArray();
        if (copy.Length != 3 || copy.Where((puzzle, index) => puzzle is null || puzzle.MaxMoves != index + 3).Any())
        {
            throw new ArgumentException("A daily set must contain the three-, four-, and five-move puzzles in that order.", nameof(puzzles));
        }

        Date = date;
        // UTC gives every player the same boundary, independent of their device's time zone.
        AvailableFromUtc = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        ExpiresAtUtc = AvailableFromUtc.AddDays(1);
        Puzzles = Array.AsReadOnly(copy);
    }

    public DateOnly Date { get; }
    public DateTimeOffset AvailableFromUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public IReadOnlyList<PuzzleDefinition> Puzzles { get; }
}
