// This class fulfills the same interface with a different calculation.
public class MultiplyOperation : IMathOperation
{
    public MultiplyOperation(int factor)
    {
        Factor = factor;

        // Each instance describes its own configured factor.
        Description = $"Multiply by {factor}";
    }

    public int Factor { get; }

    // The interface requires a readable description; its value is fixed at construction.
    public string Description { get; }

    // Callers use the shared Apply signature; this implementation performs multiplication.
    public int Apply(int input)
    {
        return input * Factor;
    }
}
