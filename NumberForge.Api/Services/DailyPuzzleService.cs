using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NumberForge.Core;

namespace NumberForge.Api.Services;

// Registered once in DI. Every request for today shares the same immutable three-puzzle set.
public sealed class DailyPuzzleService
{
    private const string IdPrefix = "daily-v1-";
    private const int SeedAttemptsPerSlot = 8;
    private readonly TimeProvider _timeProvider;
    private readonly PuzzleGenerator _generator = new PuzzleGenerator();
    private readonly Lock _syncRoot = new Lock();
    private DailyPuzzleSet? _cached;
    private DateOnly? _failedDate;
    private Exception? _generationFailure;

    public DailyPuzzleService(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    public DailyPuzzleSet GetToday(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            // A single caller prepares the new day while other callers wait for that same result.
            // At most one extra pass is needed if midnight occurs during generation.
            for (int rollover = 0; rollover < 2; rollover++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var today = CurrentDate();
                if (_cached?.Date == today)
                {
                    return _cached;
                }
                if (_failedDate == today)
                {
                    throw new InvalidOperationException("Today's puzzles could not be prepared.", _generationFailure);
                }

                DailyPuzzleSet generated;
                try
                {
                    generated = GenerateSet(today, cancellationToken);
                }
                catch (InvalidOperationException exception)
                {
                    // A deterministic exhausted search will fail the same way on every request. Cache
                    // that failure for this date so repeated requests cannot repeat expensive searches.
                    _failedDate = today;
                    _generationFailure = exception;
                    throw new InvalidOperationException("Today's puzzles could not be prepared.", exception);
                }

                if (CurrentDate() != today)
                {
                    continue;
                }

                // Replace the previous date instead of accumulating an unbounded calendar in memory.
                _cached = generated;
                _failedDate = null;
                _generationFailure = null;
                return generated;
            }
        }

        throw new InvalidOperationException("The daily boundary changed while preparing today's puzzles. Please retry.");
    }

    public PuzzleDefinition? GetCurrentById(string id, CancellationToken cancellationToken = default)
    {
        // Check the requested date before calling the generator. Unknown, old, and future IDs cannot
        // make the server construct arbitrary days or enlarge its cache.
        if (!TryParsePuzzleId(id, out var date, out int slot) || date != CurrentDate())
        {
            return null;
        }

        var set = GetToday(cancellationToken);
        return set.Date == date ? set.Puzzles[slot - 1] : null;
    }

    public static bool TryParsePuzzleId(string? id, out DateOnly date, out int slot)
    {
        date = default;
        slot = 0;
        if (id is null || id.Length != 19 || !id.StartsWith(IdPrefix, StringComparison.Ordinal)
            || id[17] != '-' || id[18] < '1' || id[18] > '3'
            || !DateOnly.TryParseExact(id.AsSpan(9, 8), "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date))
        {
            return false;
        }

        slot = id[18] - '0';
        return true;
    }

    private DateOnly CurrentDate() => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    private DailyPuzzleSet GenerateSet(DateOnly date, CancellationToken cancellationToken)
    {
        PuzzleGenerationProfile[] profiles =
        {
            PuzzleGenerationProfile.ThreeMoves,
            PuzzleGenerationProfile.FourMoves,
            PuzzleGenerationProfile.FiveMoves
        };
        var puzzles = new PuzzleDefinition[profiles.Length];
        for (int index = 0; index < profiles.Length; index++)
        {
            puzzles[index] = GenerateSlot(date, index + 1, profiles[index], cancellationToken);
        }
        return new DailyPuzzleSet(date, puzzles);
    }

    private PuzzleDefinition GenerateSlot(DateOnly date, int slot, PuzzleGenerationProfile profile,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < SeedAttemptsPerSlot; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Hashing a fixed version, date, slot, and retry number produces the same seed on every
            // server. String.GetHashCode() would change between processes and is unsuitable here.
            string input = FormattableString.Invariant($"numberforge-daily-v1|{date:yyyy-MM-dd}|{slot}|{attempt}");
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            int seed = BinaryPrimitives.ReadInt32LittleEndian(digest);
            PuzzleDefinition candidate;
            try
            {
                candidate = _generator.Generate(seed, profile, CharmType.None, cancellationToken);
            }
            catch (InvalidOperationException) when (attempt < SeedAttemptsPerSlot - 1)
            {
                // A candidate seed can exhaust its bounded search; everyone tries the same next seed.
                continue;
            }

            string id = FormattableString.Invariant($"{IdPrefix}{date:yyyyMMdd}-{slot}");
            return new PuzzleDefinition(id, $"Daily Forge {slot}", candidate.Puzzle, candidate.MaxMoves,
                candidate.MinimumMoves, candidate.TotalRoutes, candidate.Operations, candidate.Charm);
        }

        throw new InvalidOperationException("No verified puzzle could be generated for this daily slot.");
    }
}
