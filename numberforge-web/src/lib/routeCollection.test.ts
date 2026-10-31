import { afterEach, expect, test, vi } from 'vitest'
import { collectionStoragePrefix, loadDiscoveries, mergeDiscoveries, saveDiscovery } from './routeCollection'
import type { DiscoveredRoute } from './routeCollection'

afterEach(() => {
  vi.restoreAllMocks()
  window.localStorage.clear()
})

function route(letter: string, firstSessionId = 'first'): DiscoveredRoute {
  return {
    routeId: letter.repeat(64), firstSessionId, discoveredAt: 1, isPerfect: true,
    history: [{ beforeNumber: 3, operationDescription: 'Add 2 (Echo ×2)', afterNumber: 7 }],
  }
}

test('saves routes separately and preserves the first discovery when a route is repeated', () => {
  expect(saveDiscovery('puzzle', route('a'))).toBe(true)
  expect(saveDiscovery('puzzle', route('b'))).toBe(true)
  expect(saveDiscovery('puzzle', route('a', 'replay'))).toBe(true)

  const loaded = loadDiscoveries('puzzle')
  expect(loaded.storageAvailable).toBe(true)
  expect(loaded.routes).toHaveLength(2)
  expect(loaded.routes[0].firstSessionId).toBe('first')
  expect(mergeDiscoveries(loaded.routes, [route('a')])).toHaveLength(2)
  expect(loadDiscoveries('another-version').routes).toEqual([])
})

test('ignores corrupt records and mismatched identifiers without losing valid discoveries', () => {
  saveDiscovery('puzzle', route('a'))
  const prefix = collectionStoragePrefix('puzzle')
  window.localStorage.setItem(prefix + 'b'.repeat(64), '{broken')
  window.localStorage.setItem(prefix + 'c'.repeat(64), JSON.stringify({ ...route('c'), history: [null] }))
  window.localStorage.setItem(prefix + 'd'.repeat(64), JSON.stringify(route('e')))
  saveDiscovery('other-puzzle', route('f'))

  expect(loadDiscoveries('puzzle').routes.map(item => item.routeId)).toEqual(['a'.repeat(64)])
  expect(loadDiscoveries('other-puzzle').routes.map(item => item.routeId)).toEqual(['f'.repeat(64)])
})

test('reports blocked storage instead of throwing', () => {
  vi.spyOn(window, 'localStorage', 'get').mockImplementation(() => {
    throw new DOMException('Storage blocked', 'SecurityError')
  })

  expect(loadDiscoveries('puzzle')).toEqual({ routes: [], storageAvailable: false })
  expect(saveDiscovery('puzzle', route('a'))).toBe(false)
})
