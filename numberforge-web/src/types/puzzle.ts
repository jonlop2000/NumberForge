export type OperationResponse = {
    id: number,
    description: string
}

// Matches one completed move in the API's history JSON.
export type MoveResponse = {
    beforeNumber: number,
    operationDescription: string,
    afterNumber: number
}

export type PuzzleResponse = {
    startNumber: number,
    targetNumber: number,
    maxMoves: number,
    operations: OperationResponse[]
}

// A session describes one player's attempt, including its current progress and remaining cards.
export type SessionResponse = {
    // ASP.NET serializes a C# Guid as a JSON string.
    sessionId: string,
    currentNumber: number,
    targetNumber: number,
    movesUsed: number,
    maxMoves: number,
    availableOperations: OperationResponse[],
    // The API supplies the completed moves in chronological order.
    history: MoveResponse[]
}
