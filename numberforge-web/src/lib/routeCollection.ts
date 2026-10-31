import type { MoveResponse, SessionResponse } from '../types/puzzle'

export type DiscoveredRoute = {
  routeId: string
  firstSessionId: string
  discoveredAt: number
  isPerfect: boolean | null
  history: MoveResponse[]
}

export function collectionStoragePrefix(collectionKey: string) {
  return `numberforge.routes.v1.${collectionKey}.`
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isMove(value: unknown): value is MoveResponse {
  return isRecord(value)
    && typeof value.beforeNumber === 'number' && Number.isFinite(value.beforeNumber)
    && typeof value.afterNumber === 'number' && Number.isFinite(value.afterNumber)
    && typeof value.operationDescription === 'string'
}

function decodeRoute(encoded: string | null): DiscoveredRoute | null {
  try {
    const value: unknown = JSON.parse(encoded ?? 'null')
    if (!isRecord(value)
      || typeof value.routeId !== 'string' || !/^[a-f0-9]{64}$/.test(value.routeId)
      || typeof value.firstSessionId !== 'string' || value.firstSessionId.length === 0
      || typeof value.discoveredAt !== 'number' || !Number.isFinite(value.discoveredAt)
      || !(value.isPerfect === null || typeof value.isPerfect === 'boolean')
      || !Array.isArray(value.history) || !value.history.every(isMove)) {
      return null
    }
    return value as DiscoveredRoute
  } catch {
    // A damaged or older entry must not prevent the player from starting a game.
    return null
  }
}

export function mergeDiscoveries(...groups: DiscoveredRoute[][]) {
  const unique = new Map<string, DiscoveredRoute>()
  for (const route of groups.flat()) {
    if (!unique.has(route.routeId)) {
      unique.set(route.routeId, route)
    }
  }
  return [...unique.values()].sort((left, right) =>
    left.discoveredAt - right.discoveredAt || left.routeId.localeCompare(right.routeId))
}

export function loadDiscoveries(collectionKey: string) {
  try {
    const prefix = collectionStoragePrefix(collectionKey)
    const routes: DiscoveredRoute[] = []
    const storage = window.localStorage
    for (let index = 0; index < storage.length; index++) {
      const key = storage.key(index)
      if (key?.startsWith(prefix)) {
        const route = decodeRoute(storage.getItem(key))
        if (route !== null && key === prefix + route.routeId) {
          routes.push(route)
        }
      }
    }
    return { routes: mergeDiscoveries(routes), storageAvailable: true }
  } catch {
    return { routes: [] as DiscoveredRoute[], storageAvailable: false }
  }
}

export function saveDiscovery(collectionKey: string, route: DiscoveredRoute) {
  try {
    // One key per route avoids replacing other discoveries when another tab saves a win.
    const key = collectionStoragePrefix(collectionKey) + route.routeId
    const storage = window.localStorage
    const existing = decodeRoute(storage.getItem(key))
    if (existing?.routeId !== route.routeId) {
      storage.setItem(key, JSON.stringify(route))
    }
    return true
  } catch {
    return false
  }
}

export function discoveryFromWin(session: SessionResponse): DiscoveredRoute | null {
  if (!session.collectionKey || !session.routeId || session.currentNumber !== session.targetNumber) {
    return null
  }
  return {
    routeId: session.routeId,
    firstSessionId: session.sessionId,
    discoveredAt: Date.now(),
    isPerfect: session.isPerfect,
    // Preserve the discovered journey even as App replaces its current session snapshot.
    history: session.history.map(move => ({ ...move })),
  }
}
