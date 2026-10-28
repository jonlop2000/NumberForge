namespace NumberForge.Tests;

public class GameSessionTests
{
    [Fact]
    public void Constructor_InitializesSessionState()
    {
        // Arrange: define a puzzle that starts at 8 and targets 2.
        var puzzle = new Puzzle(8, 2);

        // Act: create an attempt with a limit of two moves.
        var session = new GameSession(puzzle, 2);

        // Assert: verify the starting number, unused moves, and move limit.
        Assert.Equal(8, session.CurrentNumber);
        Assert.Equal(0, session.MovesUsed);
        Assert.Equal(2, session.MaxMoves);

        // Assert.Same checks that the session stores this exact puzzle object.
        Assert.Same(puzzle, session.Puzzle);
    }

    [Fact]
    public void TryApplyOperation_WhenSessionIsActive_AppliesMove()
    {
        // Arrange: start at 8 with a target of 2 and two available moves.
        var session = new GameSession(new Puzzle(8, 2), 2);
        var subtractThree = new MathOperation(-3);

        // Act: attempt one move.
        bool accepted = session.TryApplyOperation(subtractThree);

        // Assert: the move was accepted, changed the number, and used one move.
        Assert.True(accepted);
        Assert.Equal(5, session.CurrentNumber);
        Assert.Equal(1, session.MovesUsed);
    }

    [Fact]
    public void TryApplyOperation_OnFinalAllowedMove_CanReachTarget()
    {
        // Arrange: apply the first move so the session is at 5 with one move left.
        var session = new GameSession(new Puzzle(8, 2), 2);
        var subtractThree = new MathOperation(-3);
        session.TryApplyOperation(subtractThree);

        // Act: attempt the final allowed move, taking 5 to the target of 2.
        bool accepted = session.TryApplyOperation(subtractThree);

        // Assert: reaching the target on the last available move is valid.
        Assert.True(accepted);
        Assert.Equal(2, session.CurrentNumber);
        Assert.Equal(2, session.MovesUsed);
    }

    [Fact]
    public void TryApplyOperation_WhenMoveLimitIsReached_RejectsMoveWithoutChangingState()
    {
        // Arrange: use the only allowed move. The number is 5, so the target is not reached.
        // This isolates the move-limit guard from the target-reached guard.
        var session = new GameSession(new Puzzle(8, 2), 1);
        var subtractThree = new MathOperation(-3);
        session.TryApplyOperation(subtractThree);

        // Act: attempt another move after the budget has been exhausted.
        bool accepted = session.TryApplyOperation(subtractThree);

        // Assert: rejection preserves both the current number and the move count.
        Assert.False(accepted);
        Assert.Equal(5, session.CurrentNumber);
        Assert.Equal(1, session.MovesUsed);
    }

    [Fact]
    public void TryApplyOperation_WhenTargetIsReached_RejectsMoveWithoutChangingState()
    {
        // Arrange: reach the target in two moves with a budget of three.
        // One move remains, so rejection must come from the target-reached guard.
        var session = new GameSession(new Puzzle(8, 2), 3);
        var subtractThree = new MathOperation(-3);
        session.TryApplyOperation(subtractThree);
        session.TryApplyOperation(subtractThree);

        // Act: attempt to continue playing after reaching the target.
        bool accepted = session.TryApplyOperation(subtractThree);

        // Assert: the solved session rejects the move and preserves its state.
        Assert.False(accepted);
        Assert.Equal(2, session.CurrentNumber);
        Assert.Equal(2, session.MovesUsed);
    }

    [Fact]
    public void TryApplyOperation_WithDifferentOperationTypes_ReachesTarget()
    {
        // Arrange: both concrete operation types are referenced through the same interface.
        var session = new GameSession(new Puzzle(3, 16), 2);
        IMathOperation addFive = new MathOperation(5);
        IMathOperation multiplyByTwo = new MultiplyOperation(2);

        // Act: the session calls Apply through the contract: 3 + 5 = 8, then 8 * 2 = 16.
        bool firstAccepted = session.TryApplyOperation(addFive);
        bool secondAccepted = session.TryApplyOperation(multiplyByTwo);

        // Assert: both moves were accepted and together reached the target in two moves.
        Assert.True(firstAccepted);
        Assert.True(secondAccepted);
        Assert.Equal(16, session.CurrentNumber);
        Assert.Equal(2, session.MovesUsed);
    }
}
