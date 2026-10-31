using NumberForge.Core;

namespace NumberForge.Tests;

public class EchoCharmTests
{
    [Fact]
    public void TryApplyOperation_WithEcho_ReachesTargetInTwoCardPlays()
    {
        var addTwo = new MathOperation(2);
        var multiplyByTwo = new MultiplyOperation(2);
        var addFour = new MathOperation(4);
        var session = new GameSession(new Puzzle(3, 14), 2,
            new IMathOperation[] { addTwo, multiplyByTwo, addFour }, CharmType.Echo);

        // Echo repeats +2: 3 -> 5 -> 7, while consuming just one card and one move.
        Assert.True(session.TryApplyOperation(addTwo));

        Assert.Equal(7, session.CurrentNumber);
        Assert.Equal(1, session.MovesUsed);
        Assert.True(session.EchoUsed);
        Assert.Equal(new IMathOperation[] { multiplyByTwo, addFour }, session.AvailableOperations);
        var echoedMove = Assert.Single(session.History);
        Assert.Equal(3, echoedMove.BeforeNumber);
        Assert.Equal("Add 2 (Echo ×2)", echoedMove.OperationDescription);
        Assert.Equal(7, echoedMove.AfterNumber);

        Assert.True(session.TryApplyOperation(multiplyByTwo));

        Assert.Equal(14, session.CurrentNumber);
        Assert.True(session.Puzzle.IsTargetReached(session.CurrentNumber));
        Assert.Equal(2, session.MovesUsed);
        Assert.Equal(2, session.History.Count);
        Assert.Same(addFour, Assert.Single(session.AvailableOperations));
        Assert.Equal("Multiply by 2", session.History[1].OperationDescription);
    }

    [Fact]
    public void TryApplyOperation_AfterEchoIsUsed_AppliesLaterAdditionOnce()
    {
        var addTwo = new MathOperation(2);
        var addFour = new MathOperation(4);
        var session = new GameSession(new Puzzle(3, 50), 2,
            new IMathOperation[] { addTwo, addFour }, CharmType.Echo);

        Assert.True(session.TryApplyOperation(addTwo));
        Assert.True(session.TryApplyOperation(addFour));

        // The first card adds four in total; the second adds its normal amount.
        Assert.Equal(11, session.CurrentNumber);
        Assert.True(session.EchoUsed);
        Assert.Equal(2, session.MovesUsed);
        Assert.Empty(session.AvailableOperations);
        Assert.Equal("Add 4", session.History[1].OperationDescription);

        // The annotation belongs to the move; the reusable card description stays intact.
        Assert.Equal("Add 2", addTwo.Description);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(0)]
    public void TryApplyOperation_WithMultiplicationOrNonpositiveAddition_PreservesEcho(int amount)
    {
        var multiplyByTwo = new MultiplyOperation(2);
        var nonpositiveAddition = new MathOperation(amount);
        var addThree = new MathOperation(3);
        var session = new GameSession(new Puzzle(3, 100), 3,
            new IMathOperation[] { multiplyByTwo, nonpositiveAddition, addThree }, CharmType.Echo);

        Assert.True(session.TryApplyOperation(multiplyByTwo));
        Assert.Equal(6, session.CurrentNumber);
        Assert.False(session.EchoUsed);

        Assert.True(session.TryApplyOperation(nonpositiveAddition));
        Assert.Equal(6 + amount, session.CurrentNumber);
        Assert.False(session.EchoUsed);
        Assert.Equal(nonpositiveAddition.Description, session.History[1].OperationDescription);

        // Neither earlier card spends Echo, so the later positive addition still repeats.
        Assert.True(session.TryApplyOperation(addThree));
        Assert.Equal(12 + amount, session.CurrentNumber);
        Assert.True(session.EchoUsed);
        Assert.Equal(3, session.MovesUsed);
    }

    [Fact]
    public void TryApplyOperation_WhenPositiveCardIsNotInHand_DoesNotSpendEcho()
    {
        var ownedCard = new MathOperation(2);
        var foreignCard = new MathOperation(2);
        var session = new GameSession(new Puzzle(3, 14), 2,
            new IMathOperation[] { ownedCard }, CharmType.Echo);

        Assert.False(session.TryApplyOperation(foreignCard));

        Assert.Equal(3, session.CurrentNumber);
        Assert.Equal(0, session.MovesUsed);
        Assert.False(session.EchoUsed);
        Assert.Empty(session.History);
        Assert.Same(ownedCard, Assert.Single(session.AvailableOperations));

        Assert.True(session.TryApplyOperation(ownedCard));
        Assert.Equal(7, session.CurrentNumber);
        Assert.True(session.EchoUsed);
    }

    [Theory]
    [InlineData(14, 0)] // The move budget is exhausted.
    [InlineData(3, 2)]  // The starting number already equals the target.
    public void TryApplyOperation_WhenSessionHasEnded_DoesNotSpendEcho(int target, int maxMoves)
    {
        var addTwo = new MathOperation(2);
        var session = new GameSession(new Puzzle(3, target), maxMoves,
            new IMathOperation[] { addTwo }, CharmType.Echo);

        Assert.False(session.TryApplyOperation(addTwo));

        Assert.Equal(3, session.CurrentNumber);
        Assert.Equal(0, session.MovesUsed);
        Assert.False(session.EchoUsed);
        Assert.Empty(session.History);
        Assert.Same(addTwo, Assert.Single(session.AvailableOperations));
    }

    [Fact]
    public void TryApplyOperation_WhenFirstActivationHitsTarget_ResolvesEchoBeforeCheckingWin()
    {
        var addTwo = new MathOperation(2);
        var subtractTwo = new MathOperation(-2);
        var session = new GameSession(new Puzzle(3, 5), 2,
            new IMathOperation[] { addTwo, subtractTwo }, CharmType.Echo);

        // Passing through 5 does not end the card early: Echo finishes at 7.
        Assert.True(session.TryApplyOperation(addTwo));
        Assert.Equal(7, session.CurrentNumber);
        Assert.False(session.Puzzle.IsTargetReached(session.CurrentNumber));

        Assert.True(session.TryApplyOperation(subtractTwo));
        Assert.Equal(5, session.CurrentNumber);
        Assert.True(session.Puzzle.IsTargetReached(session.CurrentNumber));
        Assert.Equal(2, session.MovesUsed);
    }
}
