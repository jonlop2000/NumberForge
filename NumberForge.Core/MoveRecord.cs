namespace NumberForge.Core;

public class MoveRecord
{
    public MoveRecord(int operationId, int beforeNumber, string operationDescription, int afterNumber)
    {
        OperationId = operationId;
        BeforeNumber = beforeNumber;
        OperationDescription = operationDescription;
        AfterNumber = afterNumber;
    }

    // Original hand position identifies the card even when two cards calculate the same thing.
    public int OperationId { get; }
    public int BeforeNumber { get; }
    public string OperationDescription { get; }
    public int AfterNumber { get; }
}
