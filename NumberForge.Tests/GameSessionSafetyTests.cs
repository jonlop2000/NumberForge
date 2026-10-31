using NumberForge.Core;

namespace NumberForge.Tests;

public class GameSessionSafetyTests
{
    [Fact]
    public void CardDescriptions_DoNotDependOnTheServerCulture()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        var customCulture = (System.Globalization.CultureInfo)originalCulture.Clone();
        customCulture.NumberFormat.NegativeSign = "minus";
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = customCulture;
            Assert.Equal("Add -3", new MathOperation(-3).Description);
            Assert.Equal("Multiply by -2", new MultiplyOperation(-2).Description);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void BuiltInArithmetic_ThrowsInsteadOfWrappingOutsideTheIntegerRange()
    {
        Assert.Throws<OverflowException>(() => new MathOperation(1).Apply(int.MaxValue));
        Assert.Throws<OverflowException>(() => new MathOperation(-1).Apply(int.MinValue));
        Assert.Throws<OverflowException>(() => new MultiplyOperation(2).Apply(int.MaxValue));
        Assert.Throws<OverflowException>(() => new MultiplyOperation(-1).Apply(int.MinValue));
    }

    [Fact]
    public void OverflowOnTheSecondEchoActivation_DoesNotPartiallyCommitTheMove()
    {
        var addOne = new MathOperation(1);
        var subtractOne = new MathOperation(-1);
        var game = new GameSession(new Puzzle(int.MaxValue - 1, int.MaxValue), 2,
            new IMathOperation[] { addOne, subtractOne }, CharmType.Echo);

        // The first activation reaches the target, but Echo must finish too. Its overflow rejects the whole card.
        Assert.Null(game.PreviewOperation(addOne));
        Assert.False(game.TryApplyOperation(addOne));
        Assert.Equal(int.MaxValue - 1, game.CurrentNumber);
        Assert.Equal(0, game.MovesUsed);
        Assert.False(game.EchoUsed);
        Assert.Empty(game.History);
        Assert.Equal(2, game.AvailableOperations.Count);

        // A valid route remains possible. The rejected card and Echo were not spent.
        Assert.True(game.TryApplyOperation(subtractOne));
        Assert.True(game.TryApplyOperation(addOne));
        Assert.Equal(int.MaxValue, game.CurrentNumber);
        Assert.True(game.EchoUsed);
        Assert.True(game.IsFinished);
    }

    [Fact]
    public void CardOwnership_UsesObjectIdentityRatherThanCustomEquality()
    {
        var first = new EqualLookingOperation(2);
        var second = new EqualLookingOperation(3);
        var foreign = new EqualLookingOperation(7);
        var game = new GameSession(new Puzzle(0, 20), 3, new IMathOperation[] { first, second });

        Assert.False(game.TryApplyOperation(foreign));
        Assert.Null(game.PreviewOperation(foreign));
        Assert.True(game.TryApplyOperation(second));
        Assert.Same(first, Assert.Single(game.AvailableOperations));
        Assert.True(game.TryApplyOperation(first));
        Assert.Equal(new[] { 2, 1 }, game.History.Select(move => move.OperationId));
        Assert.Equal(5, game.CurrentNumber);
    }

    [Fact]
    public void Constructor_RejectsInvalidRulesAndAmbiguousHands()
    {
        var puzzle = new Puzzle(0, 10);
        var card = new MathOperation(2);
        Assert.Throws<ArgumentNullException>(() => new GameSession((PuzzleDefinition)null!));
        Assert.Throws<ArgumentNullException>(() => new GameSession(null!, 1, new[] { card }));
        Assert.Throws<ArgumentNullException>(() => new GameSession(puzzle, 1, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameSession(puzzle, -1, new[] { card }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameSession(puzzle, 1, new[] { card }, (CharmType)99));
        Assert.Throws<ArgumentException>(() => new GameSession(puzzle, 2, new IMathOperation[] { null! }));
        Assert.Throws<ArgumentException>(() => new GameSession(puzzle, 2, new IMathOperation[] { card, card }));
    }

    private sealed class EqualLookingOperation(int amount) : IMathOperation
    {
        public string Description => $"Add {amount}";
        public int Apply(int input) => checked(input + amount);
        public override bool Equals(object? other) => other is EqualLookingOperation;
        public override int GetHashCode() => 0;
    }
}
