namespace NumberForge.Api.Contracts;

// Capture the charm's display details and usage at the moment this response is created.
public class CharmResponse
{
    public CharmResponse(string name, string description, bool isUsed)
    {
        Name = name;
        Description = description;
        IsUsed = isUsed;
    }

    public string Name { get; }
    public string Description { get; }
    public bool IsUsed { get; }
}
