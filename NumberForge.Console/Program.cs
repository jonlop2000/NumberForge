using NumberForge.Core;

var puzzle = new Puzzle(3, 12);

List<IMathOperation> startingOperations = new List<IMathOperation>
{
    new MathOperation(5),
    new MultiplyOperation(2),
    new MathOperation(-4)
};

var session = new GameSession(puzzle, 3, startingOperations);

// A hand can run out before the move limit, so require a card before showing the menu.
while (!puzzle.IsTargetReached(session.CurrentNumber)
    && session.MovesUsed < session.MaxMoves
    && session.AvailableOperations.Count > 0)
{
    Console.WriteLine("Available operations:");

    for (int i = 0; i < session.AvailableOperations.Count; i++)
    {
        Console.WriteLine($"{i + 1}. {session.AvailableOperations[i].Description}");
    }

    Console.WriteLine("Enter a number, or q to quit:");
    string? command = Console.ReadLine();

    if (command == "q" || command == null)
    {
        Console.WriteLine("Exiting the program.");
        return;
    }

    else if (int.TryParse(command, out int choice))
    {
        Console.WriteLine($"Parsed choice: {choice}");

        if (choice >= 1 && choice <= session.AvailableOperations.Count)
        {
            var selectedOperation = session.AvailableOperations[choice - 1];

            bool accepted = session.TryApplyOperation(selectedOperation);

            if (accepted)
            {
                Console.WriteLine($"Current number: {session.CurrentNumber}");
            }
        }
        else
        {
            Console.WriteLine("Invalid choice. Enter a number from the list.");
        }
    }
    else
    {
        Console.WriteLine("Invalid input. Enter a whole number, or q to quit.");
    }
}

if (puzzle.IsTargetReached(session.CurrentNumber))
{
    Console.WriteLine("You've reached the target number!");
}
else if (session.AvailableOperations.Count == 0)
{
    // Explain why play stopped when there are no cards left to choose.
    Console.WriteLine("You've run out of cards.");
}
else
{
    Console.WriteLine("You've run out of moves.");
}
