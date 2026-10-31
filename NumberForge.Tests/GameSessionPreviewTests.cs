using NumberForge.Core;

namespace NumberForge.Tests;

public class GameSessionPreviewTests
{
    [Fact]
    public void PreviewOperation_WithEcho_PredictsEachCardWithoutChangingTheSession()
    {
        var addTwo = new MathOperation(2);
        var multiplyByTwo = new MultiplyOperation(2);
        var addFour = new MathOperation(4);
        var cards = new IMathOperation[] { addTwo, multiplyByTwo, addFour };
        var session = new GameSession(new Puzzle(3, 14), 2, cards, CharmType.Echo);

        // Looking at one card must not spend Echo before we preview another card.
        Assert.Equal(new int?[] { 7, 6, 11 }, cards.Select(session.PreviewOperation));
        Assert.Equal(7, session.PreviewOperation(addTwo));

        Assert.Equal(3, session.CurrentNumber);
        Assert.Equal(0, session.MovesUsed);
        Assert.False(session.EchoUsed);
        Assert.Empty(session.History);
        Assert.Equal(cards, session.AvailableOperations);
        Assert.Equal(cards, session.StartingOperations);

        // The real move must still produce the result promised by the preview.
        Assert.True(session.TryApplyOperation(addTwo));
        Assert.Equal(7, session.CurrentNumber);
        Assert.Equal(1, session.MovesUsed);
        Assert.True(session.EchoUsed);
    }

    [Fact]
    public void PreviewOperation_AfterEchoIsUsed_UsesCurrentStateAndPreservesExistingHistory()
    {
        var addThree = new MathOperation(3);
        var multiplyByTwo = new MultiplyOperation(2);
        var addFour = new MathOperation(4);
        var session = new GameSession(new Puzzle(3, 50), 3,
            new IMathOperation[] { addThree, multiplyByTwo, addFour }, CharmType.Echo);
        Assert.True(session.TryApplyOperation(addThree));
        var recordedMove = Assert.Single(session.History);

        // We are now at 9 with Echo spent. +4 adds four, and the used +3 card is unavailable.
        Assert.Equal(13, session.PreviewOperation(addFour));
        Assert.Equal(18, session.PreviewOperation(multiplyByTwo));
        Assert.Null(session.PreviewOperation(addThree));

        Assert.Equal(9, session.CurrentNumber);
        Assert.Equal(1, session.MovesUsed);
        Assert.True(session.EchoUsed);
        Assert.Equal(new IMathOperation[] { multiplyByTwo, addFour }, session.AvailableOperations);
        Assert.Same(recordedMove, Assert.Single(session.History));

        Assert.True(session.TryApplyOperation(addFour));
        Assert.Equal(13, session.CurrentNumber);
    }

    [Theory]
    [InlineData(CharmType.None, 2, 5)]
    [InlineData(CharmType.Echo, -2, 1)]
    [InlineData(CharmType.Echo, 0, 3)]
    public void PreviewOperation_WhenAdditionDoesNotTriggerEcho_PredictsOneActivation(
        CharmType charm, int amount, int expectedNumber)
    {
        var card = new MathOperation(amount);
        var session = new GameSession(new Puzzle(3, 14), 2,
            new IMathOperation[] { card }, charm);

        Assert.Equal(expectedNumber, session.PreviewOperation(card));
        Assert.Equal(3, session.CurrentNumber);
        Assert.Equal(0, session.MovesUsed);
        Assert.False(session.EchoUsed);
        Assert.Empty(session.History);
        Assert.Same(card, Assert.Single(session.AvailableOperations));

        Assert.True(session.TryApplyOperation(card));
        Assert.Equal(expectedNumber, session.CurrentNumber);
    }

    [Fact]
    public void PreviewOperation_WhenCardIsNotInHand_ReturnsNullWithoutChangingTheSession()
    {
        var ownedCard = new MathOperation(2);
        var foreignCard = new MathOperation(2);
        var session = new GameSession(new Puzzle(3, 14), 2,
            new IMathOperation[] { ownedCard }, CharmType.Echo);

        // Matching arithmetic does not make a different card part of this game's hand.
        Assert.Null(session.PreviewOperation(foreignCard));
        Assert.Equal(3, session.CurrentNumber);
        Assert.Equal(0, session.MovesUsed);
        Assert.False(session.EchoUsed);
        Assert.Empty(session.History);
        Assert.Same(ownedCard, Assert.Single(session.AvailableOperations));
        Assert.Equal(7, session.PreviewOperation(ownedCard));
    }

    [Theory]
    [InlineData(7, 3)]  // The target was reached while moves remain.
    [InlineData(14, 1)] // The move limit was reached without solving the puzzle.
    public void PreviewOperation_WhenGameHasEnded_ReturnsNullForAnUnusedCard(int target, int maxMoves)
    {
        var addTwo = new MathOperation(2);
        var addFour = new MathOperation(4);
        var session = new GameSession(new Puzzle(3, target), maxMoves,
            new IMathOperation[] { addTwo, addFour }, CharmType.Echo);
        Assert.True(session.TryApplyOperation(addTwo));
        var recordedMove = Assert.Single(session.History);

        Assert.Null(session.PreviewOperation(addFour));

        Assert.Equal(7, session.CurrentNumber);
        Assert.Equal(1, session.MovesUsed);
        Assert.True(session.EchoUsed);
        Assert.Same(addFour, Assert.Single(session.AvailableOperations));
        Assert.Same(recordedMove, Assert.Single(session.History));
    }

    [Fact]
    public void PreviewOperation_WhenEchoPassesThroughTarget_PredictsTheFullyResolvedNumber()
    {
        var addTwo = new MathOperation(2);
        var session = new GameSession(new Puzzle(3, 5), 2,
            new IMathOperation[] { addTwo }, CharmType.Echo);

        // Echo finishes at 7 even though the first activation passes through the target of 5.
        Assert.Equal(7, session.PreviewOperation(addTwo));
        Assert.Equal(3, session.CurrentNumber);
        Assert.False(session.EchoUsed);

        Assert.True(session.TryApplyOperation(addTwo));
        Assert.Equal(7, session.CurrentNumber);
        Assert.False(session.Puzzle.IsTargetReached(session.CurrentNumber));
    }
}
