namespace NumberForge.Api.Contracts;

public class OperationResponse
{
    public OperationResponse(int id, string description, int? previewNumber = null)
    {
        Id = id;
        Description = description;
        PreviewNumber = previewNumber;
    }

    public int Id { get; }
    public string Description { get; }

    // Null means no playable result is available, such as before starting or after finishing.
    public int? PreviewNumber { get; }
}
