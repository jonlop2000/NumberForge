export type OperationResponse = {
    id: number,
    description: string
}

export type PuzzleResponse = {
    startNumber: number,
    targetNumber: number,
    maxMoves: number,
    operations: OperationResponse[]
}