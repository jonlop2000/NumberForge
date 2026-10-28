using NumberForge.Core;

var puzzle = new Puzzle(3, 16);

var subtractThree = new MathOperation(-3);
var session = new GameSession(puzzle, 2);

List<IMathOperation> operations = new List<IMathOperation>
{
    new MathOperation(5),
    subtractThree,
    new MultiplyOperation(2)
};

while (!puzzle.IsTargetReached(session.CurrentNumber) && session.MovesUsed < session.MaxMoves) 
{
    Console.WriteLine("Available operations:");

    for (int i = 0; i < operations.Count; i++)
    {
        Console.WriteLine($"{i + 1}. {operations[i].Description}");
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

        if (choice >= 1 && choice <= operations.Count)
        {
            var selectedOperation = operations[choice - 1];

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
else
{
    Console.WriteLine("You've run out of moves.");
}
