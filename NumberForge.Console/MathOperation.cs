// The interface requires this class to provide Description and Apply.
public class MathOperation : IMathOperation
{
    public MathOperation(int amount)
    {
        Amount = amount;

        // Build the label from this object's configuration using string interpolation.
        Description = $"Add {amount}";
    }

    public int Amount { get; }

    // Callers using IMathOperation can read this label without knowing the concrete class.
    // The constructor assigns this get-only property when the object is created.
    public string Description { get; }

    // This public method satisfies the interface's Apply contract using addition.
    public int Apply(int input)
    {
        return input + Amount;
    }
}
