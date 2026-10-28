namespace NumberForge.Core;

public class MultiplyOperation : IMathOperation
{
    public MultiplyOperation(int factor)
    {
        Factor = factor;

        Description = $"Multiply by {factor}";
    }

    public int Factor { get; }

    public string Description { get; }

    public int Apply(int input)
    {
        return input * Factor;
    }
}
