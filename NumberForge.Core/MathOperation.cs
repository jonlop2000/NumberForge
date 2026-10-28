namespace NumberForge.Core;

public class MathOperation : IMathOperation
{
    public MathOperation(int amount)
    {
        Amount = amount;

        Description = $"Add {amount}";
    }

    public int Amount { get; }

    public string Description { get; }

    public int Apply(int input)
    {
        return input + Amount;
    }
}
