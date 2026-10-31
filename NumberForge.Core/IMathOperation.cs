namespace NumberForge.Core;

public interface IMathOperation
{
    // Keep calculations deterministic and free of state changes so previews match actual plays.
    int Apply(int input);
    string Description { get;}
}
