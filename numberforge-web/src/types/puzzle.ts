export type OperationResponse = {
  id: number
  description: string
  // Results stay hidden; this compatibility field is null in current API responses.
  previewNumber: number | null
}

export type MoveResponse = {
  beforeNumber: number
  operationDescription: string
  afterNumber: number
}

export type PuzzleResponse = {
  id: string
  name: string
  collectionKey: string
  // The server conceals the count until this player has won the puzzle.
  totalRoutes: number | null
  startNumber: number
  targetNumber: number
  maxMoves: number
  operations: OperationResponse[]
}

export type DailyPuzzleSetResponse = {
  date: string
  availableFromUtc: string
  expiresAtUtc: string
  puzzles: PuzzleResponse[]
}

export type PuzzleProgressResponse = {
  puzzleId: string
  collectionKey: string
  hasSolved: boolean
  totalRoutes: number | null
  foundRoutes: number
  bestMoves: number | null
  routeIds: string[]
}

export type CharmResponse = {
  name: string
  description: string
  isUsed: boolean
}

export type ApplyMoveRequest = {
  operationId: number
  expectedRevision: number
  // Reuse the same ID if the connection drops while this move is being accepted.
  requestId: string
}

export type SessionResponse = {
  sessionId: string
  puzzleId: string | null
  puzzleName: string | null
  elapsedSeconds: number | null
  isPerfect: boolean | null
  collectionKey: string | null
  totalRoutes: number | null
  routeId: string | null
  currentNumber: number
  targetNumber: number
  movesUsed: number
  maxMoves: number
  availableOperations: OperationResponse[]
  history: MoveResponse[]
  charm: CharmResponse | null
  revision: number
  playUntilUtc: string | null
  expiresAtUtc: string | null
  status: 'active' | 'won' | 'lost' | 'expired'
}
