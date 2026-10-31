namespace NumberForge.Core;

public class MathOperation : IMathOperation
{
    public MathOperation(int amount)
    {
        Amount = amount;

        // Card text is also part of a puzzle's stable identity, so server locale must not change it.
        Description = FormattableString.Invariant($"Add {amount}");
    }

    public int Amount { get; }

    public string Description { get; }

    public int Apply(int input)
    {
        return checked(input + Amount);
    }
}
