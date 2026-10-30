using NumberForge.Core;

namespace NumberForge.Tests;

public class GameSessionTests
{
    [Fact]
    public void Constructor_InitializesSessionState()
    {
        // Arrange: define a puzzle that starts at 8 and targets 2.
        var puzzle = new Puzzle(8, 2);

        // Supply three starting cards to check that the session exposes the whole hand.
        var operations = new IMathOperation[]
        {
            new MathOperation(5),
            new MultiplyOperation(2),
            new MathOperation(-4)
        };

        // Act: create an attempt with a limit of two moves.
        var session = new GameSession(puzzle, 2, operations);

        // Assert: verify the starting number, unused moves, and move limit.
        Assert.Equal(8, session.CurrentNumber);
        Assert.Equal(0, session.MovesUsed);
        Assert.Equal(2, session.MaxMoves);
        Assert.Empty(session.History);

        // Assert.Same checks that the session stores this exact puzzle object.
        Assert.Same(puzzle, session.Puzzle);

        // The starting hand preserves the supplied cards and their menu order.
        Assert.Equal(3, session.AvailableOperations.Count);
        Assert.Equal(operations, session.AvailableOperations);
    }

    [Fact]
    public void Constructor_WhenStartingListChanges_PreservesSessionHand()
    {
        // Arrange: give two sessions the same starting list.
        var addFive = new MathOperation(5);
        var operations = new List<IMathOperation> { addFive };
        var firstSession = new GameSession(new Puzzle(3, 12), 3, operations);
        var secondSession = new GameSession(new Puzzle(3, 12), 3, operations);

        // Act: edit the caller's list after both sessions have copied it.
        operations.Clear();
        operations.Add(new MultiplyOperation(2));

        // Assert: neither session loses or replaces its starting card.
        Assert.Single(firstSession.AvailableOperations);
        Assert.Single(secondSession.AvailableOperations);
        Assert.Same(addFive, firstSession.AvailableOperations[0]);
        Assert.Same(addFive, secondSession.AvailableOperations[0]);
    }

    [Fact]
    public void AvailableOperations_WhenCallerAttemptsToClearHand_RejectsChange()
    {
        // Arrange: even casting the read-only view to a collection must not allow edits.
        var operations = new IMathOperation[] { new MathOperation(5) };
        var session = new GameSession(new Puzzle(3, 12), 3, operations);
        var hand = Assert.IsAssignableFrom<ICollection<IMathOperation>>(session.AvailableOperations);

        // Act and assert: the wrapper rejects removal and the session keeps its card.
        Assert.Throws<NotSupportedException>(() => hand.Clear());
        Assert.Single(session.AvailableOperations);
    }

    [Fact]
    public void History_WhenCallerAttemptsToClear_RejectsChange()
    {
        var addFive = new MathOperation(5);
        var session = new GameSession(new Puzzle(3, 12), 3, new IMathOperation[] { addFive });

        // Capture the view before playing: it should show future entries without allowing edits.
        var history = session.History;
        Assert.True(session.TryApplyOperation(addFive));
        var recordedMove = Assert.Single(history);
        var collection = Assert.IsAssignableFrom<ICollection<MoveRecord>>(history);

        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.Same(recordedMove, Assert.Single(session.History));
    }

    [Fact]
    public void History_WhenAnotherSessionMoves_RemainsEmpty()
    {
        // Sharing a starting hand must not share the players' move histories.
        var addFive = new MathOperation(5);
        var operations = new IMathOperation[] { addFive };
        var firstSession = new GameSession(new Puzzle(3, 12), 3, operations);
        var secondSession = new GameSession(new Puzzle(3, 12), 3, operations);

        Assert.True(firstSession.TryApplyOperation(addFive));

        Assert.Single(firstSession.History);
        Assert.Empty(secondSession.History);
        Assert.Equal(3, secondSession.CurrentNumber);
        Assert.Equal(0, secondSession.MovesUsed);
    }

    [Fact]
    public void TryApplyOperation_WhenCardIsNotInHand_RejectsMoveWithoutRecordingHistory()
    {
        var addFive = new MathOperation(5);
        var otherCard = new MultiplyOperation(2);
        var session = new GameSession(new Puzzle(3, 12), 3, new IMathOperation[] { addFive });

        // A card that was never in this game must not create a history entry.
        Assert.False(session.TryApplyOperation(otherCard));

        Assert.Empty(session.History);
        Assert.Equal(3, session.CurrentNumber);
        Assert.Equal(0, session.MovesUsed);
        Assert.Same(addFive, Assert.Single(session.AvailableOperations));
    }

    [Fact]
    public void TryApplyOperation_WhenSessionIsActive_AppliesMove()
    {
        // Arrange: start at 8 with a target of 2 and two available moves.
        var subtractThree = new MathOperation(-3);
        // Include the operation in this session's starting hand.
        var session = new GameSession(new Puzzle(8, 2), 2, new IMathOperation[] { subtractThree });

        // Act: attempt one move.
        bool accepted = session.TryApplyOperation(subtractThree);

        // Assert: the move was accepted, changed the number, and used one move.
        Assert.True(accepted);
        Assert.Equal(5, session.CurrentNumber);
        Assert.Equal(1, session.MovesUsed);

        // The saved entry describes the actual transition rather than just the final number.
        var move = Assert.Single(session.History);
        Assert.Equal(8, move.BeforeNumber);
        Assert.Equal("Add -3", move.OperationDescription);
        Assert.Equal(5, move.AfterNumber);

        // Consuming the card empties the available hand while preserving its original position.
        Assert.Empty(session.AvailableOperations);
        Assert.Single(session.StartingOperations);
        Assert.Same(subtractThree, session.StartingOperations[0]);
    }

    [Fact]
    public void TryApplyOperation_WhenCardWasAlreadyUsed_RejectsMoveWithoutChangingState()
    {
        // Arrange: keep moves and other cards available so only card reuse can reject the move.
        var addFive = new MathOperation(5);
        var multiplyByTwo = new MultiplyOperation(2);
        var subtractFour = new MathOperation(-4);
        var operations = new IMathOperation[] { addFive, multiplyByTwo, subtractFour };
        var session = new GameSession(new Puzzle(3, 12), 3, operations);

        // Apply the card once and verify that it is consumed after the accepted move.
        Assert.True(session.TryApplyOperation(addFive));
        Assert.Equal(8, session.CurrentNumber);
        Assert.Equal(1, session.MovesUsed);
        Assert.Equal(2, session.AvailableOperations.Count);
        Assert.DoesNotContain(addFive, session.AvailableOperations);
        var recordedMove = Assert.Single(session.History);

        // Act: try the same card again while the session is still active.
        bool accepted = session.TryApplyOperation(addFive);

        // Assert: rejection preserves the number, move count, and remaining cards in order.
        Assert.False(accepted);
        Assert.Equal(8, session.CurrentNumber);
        Assert.Equal(1, session.MovesUsed);
        Assert.Equal(2, session.AvailableOperations.Count);
        Assert.Equal(new IMathOperation[] { multiplyByTwo, subtractFour }, session.AvailableOperations);
        Assert.Same(recordedMove, Assert.Single(session.History));
    }

    [Fact]
    public void TryApplyOperation_OnFinalAllowedMove_CanReachTarget()
    {
        // Arrange: apply the first move so the session is at 5 with one move left.
        // Identical calculations can be separate cards, one for each move.
        var firstCard = new MathOperation(-3);
        var secondCard = new MathOperation(-3);
        var session = new GameSession(new Puzzle(8, 2), 2, new IMathOperation[] { firstCard, secondCard });
        session.TryApplyOperation(firstCard);

        // Act: attempt the final allowed move, taking 5 to the target of 2.
        bool accepted = session.TryApplyOperation(secondCard);

        // Assert: reaching the target on the last available move is valid.
        Assert.True(accepted);
        Assert.Equal(2, session.CurrentNumber);
        Assert.Equal(2, session.MovesUsed);
        Assert.Equal(2, session.History.Count);
        Assert.Equal(5, session.History[1].BeforeNumber);
        Assert.Equal(2, session.History[1].AfterNumber);
    }

    [Fact]
    public void TryApplyOperation_WhenMoveLimitIsReached_RejectsMoveWithoutChangingState()
    {
        // Arrange: use the only allowed move. The number is 5, so the target is not reached.
        // This isolates the move-limit guard from the target-reached guard.
        // Leave a second card available so this test specifically checks the move limit.
        var firstCard = new MathOperation(-3);
        var secondCard = new MathOperation(-3);
        var session = new GameSession(new Puzzle(8, 2), 1, new IMathOperation[] { firstCard, secondCard });
        session.TryApplyOperation(firstCard);
        var historyBeforeRejection = session.History.ToArray();

        // Act: attempt another move after the budget has been exhausted.
        bool accepted = session.TryApplyOperation(secondCard);

        // Assert: rejection preserves both the current number and the move count.
        Assert.False(accepted);
        Assert.Equal(5, session.CurrentNumber);
        Assert.Equal(1, session.MovesUsed);
        Assert.Equal(historyBeforeRejection, session.History);
    }

    [Fact]
    public void TryApplyOperation_WhenTargetIsReached_RejectsMoveWithoutChangingState()
    {
        // Arrange: reach the target in two moves with a budget of three.
        // One move remains, so rejection must come from the target-reached guard.
        // Leave a third card available so this test specifically checks the target guard.
        var firstCard = new MathOperation(-3);
        var secondCard = new MathOperation(-3);
        var thirdCard = new MathOperation(-3);
        var session = new GameSession(new Puzzle(8, 2), 3,
            new IMathOperation[] { firstCard, secondCard, thirdCard });
        session.TryApplyOperation(firstCard);
        session.TryApplyOperation(secondCard);
        var historyBeforeRejection = session.History.ToArray();

        // Act: attempt to continue playing after reaching the target.
        bool accepted = session.TryApplyOperation(thirdCard);

        // Assert: the solved session rejects the move and preserves its state.
        Assert.False(accepted);
        Assert.Equal(2, session.CurrentNumber);
        Assert.Equal(2, session.MovesUsed);
        Assert.Equal(historyBeforeRejection, session.History);
    }

    [Fact]
    public void TryApplyOperation_WithDifferentOperationTypes_ReachesTarget()
    {
        // Arrange: both concrete operation types are referenced through the same interface.
        IMathOperation addFive = new MathOperation(5);
        IMathOperation multiplyByTwo = new MultiplyOperation(2);
        // Both kinds of operation can be supplied together as the starting hand.
        var session = new GameSession(new Puzzle(3, 16), 2,
            new IMathOperation[] { addFive, multiplyByTwo });

        // Act: the session calls Apply through the contract: 3 + 5 = 8, then 8 * 2 = 16.
        bool firstAccepted = session.TryApplyOperation(addFive);
        bool secondAccepted = session.TryApplyOperation(multiplyByTwo);

        // Assert: both moves were accepted and together reached the target in two moves.
        Assert.True(firstAccepted);
        Assert.True(secondAccepted);
        Assert.Equal(16, session.CurrentNumber);
        Assert.Equal(2, session.MovesUsed);

        // Assert.Collection checks both the number of entries and their chronological order.
        Assert.Collection(session.History,
            move =>
            {
                Assert.Equal(3, move.BeforeNumber);
                Assert.Equal("Add 5", move.OperationDescription);
                Assert.Equal(8, move.AfterNumber);
            },
            move =>
            {
                Assert.Equal(8, move.BeforeNumber);
                Assert.Equal("Multiply by 2", move.OperationDescription);
                Assert.Equal(16, move.AfterNumber);
            });
    }
}
