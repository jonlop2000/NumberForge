namespace NumberForge.Tests;

public class MathOperationTests
{
    // [Fact] tells xUnit to discover and run this method as a test.
    [Fact]
    public void Apply_WithNegativeAmount_SubtractsFromInput()
    {
        // Arrange: create a "Subtract 3" operation.
        var operation = new MathOperation(-3);

        // Act: apply it to the starting number, 8.
        int result = operation.Apply(8);

        // Assert: expected value comes first, actual result comes second.
        // xUnit fails this test automatically if the values differ.
        Assert.Equal(5, result);
    }

    [Fact]
    public void Apply_WhenReused_DoesNotChangeAmount()
    {
        // Arrange: configure one operation that we will use more than once.
        var operation = new MathOperation(-3);

        // Act: reuse the same object with different inputs.
        operation.Apply(8);
        operation.Apply(5);

        // Assert: performing calculations must preserve the configured amount.
        // This test checks the configuration, so it does not need the returned results.
        Assert.Equal(-3, operation.Amount);
    }
}
