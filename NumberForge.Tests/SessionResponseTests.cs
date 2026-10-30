using NumberForge.Api.Contracts;

namespace NumberForge.Tests;

public class SessionResponseTests
{
    [Fact]
    public void Constructor_WhenSourceHistoryChanges_PreservesReadOnlySnapshot()
    {
        var firstMove = new MoveResponse(3, "Add 5", 8);
        var history = new List<MoveResponse> { firstMove };
        var response = new SessionResponse(Guid.NewGuid(), 8, 12, 1, 3,
            Array.Empty<OperationResponse>(), history);

        // Editing the supplied list afterward must not replace the response's original entry.
        history.Clear();
        history.Add(new MoveResponse(8, "Multiply by 2", 16));

        Assert.Same(firstMove, Assert.Single(response.History));

        // The copied list must also reject edits through the response itself.
        var collection = Assert.IsAssignableFrom<ICollection<MoveResponse>>(response.History);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.Same(firstMove, Assert.Single(response.History));
    }
}
