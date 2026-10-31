import { useEffect, useRef, useState } from 'react'
import {
  collectionStoragePrefix, discoveryFromWin, loadDiscoveries, mergeDiscoveries, saveDiscovery,
} from '../lib/routeCollection'
import type { DiscoveredRoute } from '../lib/routeCollection'
import type { PuzzleProgressResponse, SessionResponse } from '../types/puzzle'

type RouteCollectionProps = {
  collectionKey: string
  progress: PuzzleProgressResponse
  session: SessionResponse | null
}

function RouteCollection({ collectionKey, progress, session }: RouteCollectionProps) {
  // Keep discoveries for this visit too, so a blocked storage write never interrupts gameplay.
  const memory = useRef(new Map<string, DiscoveredRoute[]>())
  const [collection, setCollection] = useState<{
    key: string | null; routes: DiscoveredRoute[]; storageAvailable: boolean
  }>({ key: null, routes: [], storageAvailable: true })

  useEffect(() => {
    function updateCollection(recordWin = true) {
      const loaded = loadDiscoveries(collectionKey)
      // Cached journeys are display details. Only the server can credit a discovered route.
      const verified = new Set(progress.collectionKey === collectionKey ? progress.routeIds : [])
      let routes = mergeDiscoveries(loaded.routes, memory.current.get(collectionKey) ?? [])
        .filter(route => verified.has(route.routeId))
      if (recordWin && session?.collectionKey === collectionKey) {
        const discovery = discoveryFromWin(session)
        if (discovery !== null && verified.has(discovery.routeId)) {
          routes = mergeDiscoveries(routes, [discovery])
        }
      }

      let storageAvailable = loaded.storageAvailable
      if (storageAvailable) {
        for (const route of routes) {
          if (!saveDiscovery(collectionKey, route)) {
            storageAvailable = false
            break
          }
        }
      }
      memory.current.set(collectionKey, routes)
      setCollection({ key: collectionKey, routes, storageAvailable })
    }

    updateCollection()
    function handleStorage(event: StorageEvent) {
      if (event.key === null) {
        // Clearing browser storage also clears cached collections for puzzles not currently shown.
        memory.current.clear()
        updateCollection(false)
        return
      }
      if (event.newValue === null) {
        for (const cachedKey of memory.current.keys()) {
          if (event.key.startsWith(collectionStoragePrefix(cachedKey))) {
            memory.current.delete(cachedKey)
          }
        }
      }
      if (event.key.startsWith(collectionStoragePrefix(collectionKey))) {
        updateCollection(event.newValue !== null)
      }
    }
    window.addEventListener('storage', handleStorage)
    return () => window.removeEventListener('storage', handleStorage)
  }, [collectionKey, progress, session])

  // Changing the picker must never briefly show another puzzle's count or journeys.
  const routes = collection.key === collectionKey
    ? collection.routes : []
  const currentDiscovery = session?.collectionKey === collectionKey
    ? routes.find(route => route.routeId === session.routeId) : undefined

  // A first verified win unlocks the side quest, even when this browser has no cached history.
  if (progress.collectionKey !== collectionKey || !progress.hasSolved || progress.totalRoutes === null) {
    return null
  }

  return (
    <section className="route-collection" aria-labelledby="route-collection-heading">
      <h2 id="route-collection-heading">Route collection</h2>
      <p>Routes discovered: {progress.foundRoutes}/{progress.totalRoutes}</p>
      {currentDiscovery && (
        <p className="route-discovery" role="status">
          {currentDiscovery.firstSessionId === session?.sessionId
            ? 'New route discovered!' : 'Route already discovered.'}
        </p>
      )}
      {typeof progress.bestMoves === 'number' && <p>Best route: {progress.bestMoves} {progress.bestMoves === 1 ? 'card' : 'cards'}</p>}
      {progress.foundRoutes === progress.totalRoutes && (
        <p className="collection-badge" role="status">All routes discovered!</p>
      )}
      {collection.key === collectionKey && !collection.storageAvailable && (
        <p className="game-feedback">Your route count is saved. This browser couldn’t save journey details, so those details will stay available for this visit.</p>
      )}
      {routes.length > 0 && (
        <details>
          <summary>Your discovered routes</summary>
          <ol className="saved-routes">
            {routes.map((route, index) => (
              <li key={route.routeId}>
                <details>
                  <summary>
                    Route {index + 1} · {route.history.length} {route.history.length === 1 ? 'card' : 'cards'}
                    {route.isPerfect === true ? ' · Perfect' : ''}
                  </summary>
                  <ol>
                    {route.history.map((move, moveIndex) => (
                      <li key={moveIndex}>
                        {move.beforeNumber} → {move.operationDescription} → {move.afterNumber}
                      </li>
                    ))}
                  </ol>
                </details>
              </li>
            ))}
          </ol>
        </details>
      )}
    </section>
  )
}

export default RouteCollection
