using NumberForge.Core;

namespace NumberForge.Tests;

public class PuzzleTests
{
    [Fact]
    public void IsTargetReached_WhenCurrentNumberMatchesTarget_ReturnsTrue()
    {
        // Arrange: define a puzzle that starts at 8 and targets 2.
        var puzzle = new Puzzle(8, 2);

        // Act: check a current number that matches the target.
        bool result = puzzle.IsTargetReached(2);

        // Assert: the target has been reached.
        Assert.True(result);
    }

    [Fact]
    public void IsTargetReached_WhenCurrentNumberDiffersFromTarget_ReturnsFalse()
    {
        // Arrange: use the same puzzle definition in an independent test.
        var puzzle = new Puzzle(8, 2);

        // Act: check 5, which does not match the target of 2.
        bool result = puzzle.IsTargetReached(5);

        // Assert: the target has not been reached.
        Assert.False(result);
    }

    [Fact]
    public void IsTargetReached_WhenStartAlreadyMatchesTarget_ReturnsTrue()
    {
        // Arrange: the puzzle's starting number already equals its target.
        var puzzle = new Puzzle(2, 2);

        // Act: check the starting number itself.
        bool result = puzzle.IsTargetReached(puzzle.StartNumber);

        // Assert: this edge case also counts as reaching the target.
        Assert.True(result);
    }
}
