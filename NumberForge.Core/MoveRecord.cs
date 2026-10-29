namespace NumberForge.Core;

public class MoveRecord
{
    public MoveRecord(int beforeNumber, string operationDescription, int afterNumber)
    {
        BeforeNumber = beforeNumber;
        OperationDescription = operationDescription;
        AfterNumber = afterNumber;
    }

    public int BeforeNumber { get; }
    public string OperationDescription { get; }
    public int AfterNumber { get; }
}
