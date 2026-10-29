namespace NumberForge.Api.Contracts;

public class OperationResponse
{
    public OperationResponse(int id, string description)
    {
        Id = id;
        Description = description;
    }

    public int Id { get; }
    public string Description { get; }
}
