namespace NumberForge.Api.Contracts;

public class ApplyMoveRequest
{
    public ApplyMoveRequest(int operationId)
    {
        OperationId = operationId;
    }

    public int OperationId { get; }
}
