namespace NumberForge.Api.Contracts;

public class MoveResponse
{
    public MoveResponse(int beforeNumber, string operationDescription, int afterNumber)
    {
        BeforeNumber = beforeNumber;
        OperationDescription = operationDescription;
        AfterNumber = afterNumber;
    }

    public int BeforeNumber { get; }
    public string OperationDescription { get; }
    public int AfterNumber { get; }
}