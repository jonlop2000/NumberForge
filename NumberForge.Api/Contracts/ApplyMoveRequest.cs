using System.ComponentModel.DataAnnotations;

namespace NumberForge.Api.Contracts;

public sealed class ApplyMoveRequest
{
    public ApplyMoveRequest(int operationId, int? expectedRevision = null, Guid? requestId = null)
    {
        OperationId = operationId;
        ExpectedRevision = expectedRevision;
        RequestId = requestId;
    }
    [Range(1, 8)]
    public int OperationId { get; }
    [Range(0, 8)]
    public int? ExpectedRevision { get; }
    // Reuse this ID when retrying the same move after a lost connection.
    public Guid? RequestId { get; }
}
