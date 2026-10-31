namespace NumberForge.Core;

// These pure rules are shared by real attempts and the solver. Calculating a move never spends a card.
internal static class GameRules
{
    internal static void ValidateCharm(CharmType charm)
    {
        if (charm != CharmType.None && charm != CharmType.Echo)
        {
            throw new ArgumentOutOfRangeException(nameof(charm), charm, "Supported charms are None and Echo.");
        }
    }

    internal static void ValidateHand(IReadOnlyList<IMathOperation> cards)
    {
        for (int index = 0; index < cards.Count; index++)
        {
            if (cards[index] is null)
            {
                throw new ArgumentException("A hand cannot contain a null card.", nameof(cards));
            }

            // Two equal-looking cards may be distinct objects, but one object cannot have two card IDs.
            for (int previous = 0; previous < index; previous++)
            {
                if (ReferenceEquals(cards[index], cards[previous]))
                {
                    throw new ArgumentException("Each position in a hand must contain a distinct card instance.", nameof(cards));
                }
            }
        }
    }

    internal static bool IsFinished(Puzzle puzzle, int currentNumber, int movesUsed, int maxMoves, int remainingCards)
    {
        return puzzle.IsTargetReached(currentNumber) || movesUsed >= maxMoves || remainingCards == 0;
    }

    internal static bool TryCalculate(int currentNumber, IMathOperation operation, CharmType charm, bool echoUsed,
        out int afterNumber, out bool usesEcho)
    {
        afterNumber = currentNumber;
        usesEcho = false;
        try
        {
            int result = operation.Apply(currentNumber);
            bool repeats = charm == CharmType.Echo && !echoUsed
                && operation is MathOperation addition && addition.Amount > 0;
            if (repeats)
            {
                result = operation.Apply(result);
            }

            // Both activations must succeed before the caller can commit any of the move's effects.
            afterNumber = result;
            usesEcho = repeats;
            return true;
        }
        catch (OverflowException)
        {
            // Numbers outside the supported integer range are illegal, rather than wrapping into another value.
            return false;
        }
    }
}
