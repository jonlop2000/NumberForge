using NumberForge.Core;

namespace NumberForge.Tests;

public class PuzzleGenerationProfileTests
{
    [Theory]
    [InlineData(2, 1, 1, 2, 4, "maxMoves")]
    [InlineData(6, 2, 3, 2, 4, "maxMoves")]
    [InlineData(4, 0, 3, 2, 4, "minimumSolutionMoves")]
    [InlineData(4, 4, 4, 2, 4, "minimumSolutionMoves")]
    [InlineData(4, 3, 2, 2, 4, "maximumSolutionMoves")]
    [InlineData(4, 2, 4, 2, 4, "maximumSolutionMoves")]
    [InlineData(4, 2, 3, 1, 4, "minimumRoutes")]
    [InlineData(4, 2, 3, 3, 2, "maximumRoutes")]
    public void InvalidSettings_AreRejectedBeforeGeneration(int maxMoves, int minimumSolutionMoves,
        int maximumSolutionMoves, int minimumRoutes, int maximumRoutes, string parameterName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new PuzzleGenerationProfile(
            maxMoves, minimumSolutionMoves, maximumSolutionMoves, minimumRoutes, maximumRoutes));

        Assert.Equal(parameterName, exception.ParamName);
    }
}
