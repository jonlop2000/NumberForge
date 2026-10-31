namespace NumberForge.Core;

public class MultiplyOperation : IMathOperation
{
    public MultiplyOperation(int factor)
    {
        Factor = factor;

        Description = FormattableString.Invariant($"Multiply by {factor}");
    }

    public int Factor { get; }

    public string Description { get; }

    public int Apply(int input)
    {
        return checked(input * Factor);
    }
}
