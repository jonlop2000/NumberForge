namespace NumberForge.Core;

public class Puzzle
{
    public Puzzle(int startNumber, int targetNumber)
    {
        StartNumber = startNumber;
        TargetNumber = targetNumber;
    }

    public int StartNumber { get; }
    public int TargetNumber { get; }

    public bool IsTargetReached(int currentNumber)
    {
        return currentNumber == TargetNumber;
    }
}
