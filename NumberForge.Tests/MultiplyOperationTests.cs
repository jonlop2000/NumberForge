using NumberForge.Core;

namespace NumberForge.Tests;

public class MultiplyOperationTests
{
    [Fact]
    public void Apply_WithPositiveFactor_MultipliesInput()
    {
        // Arrange: create a reusable "Multiply by 3" operation.
        var operation = new MultiplyOperation(3);

        // Act: apply the configured factor to the input, 4.
        int result = operation.Apply(4);

        // Assert: the calculation returns 12 and preserves the configured factor.
        // Assert.Equal takes the expected value first and the actual value second.
        Assert.Equal(12, result);
        Assert.Equal(3, operation.Factor);
    }
}
