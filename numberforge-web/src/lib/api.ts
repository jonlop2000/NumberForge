// Same-origin cookies identify the player. No credential belongs in localStorage.
export function apiFetch(path: string, init: RequestInit = {}) {
  return fetch(path, { ...init, credentials: 'same-origin', cache: 'no-store' })
}

export function failureMessage(response: Response, fallback: string) {
  if (response.status === 429) {
    const seconds = Number(response.headers.get('Retry-After'))
    return Number.isFinite(seconds) && seconds > 0
      ? `Too many requests. Try again in ${Math.ceil(seconds)} seconds.`
      : 'Too many requests. Please wait a moment and try again.'
  }
  if (response.status === 503) return 'The game service is temporarily unavailable. Please try again shortly.'
  if (response.status === 403) return 'The game could not verify this request. Reload the page and try again.'
  return fallback
}
