# Number Forge: gameplay and backend launch preparation

The backend supports a shared daily set, reusable practice puzzles, player-owned attempts,
and server-verified route discoveries. The React application now connects these rules through
its existing layout and styles: the picker groups Today's three puzzles and four Practice puzzles,
reads verified progress, and sends move revisions and request IDs.

## The player experience

- Everyone receives the same three daily puzzles, with budgets of three, four, and five moves.
  Their hands contain four, five, and six single-use cards respectively.
- Each daily puzzle has at least two winning routes. The three profiles limit their route counts
  to 2–4, 2–8, and 2–12, keeping the optional collection quest manageable.
- Every generated puzzle has a shorter solution than its full move budget. The exact minimum
  stays on the server; a winning attempt receives `isPerfect` without spoiling that minimum.
- Route counts remain `null` until that player has won that puzzle. Afterward, replays can improve
  the move count or discover different ordered card sequences. Repeating a known route is legal
  but never increases the collection count.
- A move spends one original card ID. Winning ends the attempt immediately. Echo, where present
  in practice, repeats the first positive addition once, using one card and one move.
- There is no undo, countdown, numeric preview, or time-based score. Timing is hidden while
  playing and frozen at the accepted finishing move.
- A shared UTC date controls the daily reset. An attempt admitted before midnight must submit
  its next move before the cutoff. Old daily puzzles cannot be started or continued afterward.
  Completed results remain readable until the following UTC midnight. Four fixed practice
  puzzles remain available independently of the daily cycle.
- The old arbitrary generation endpoint returns HTTP 410. It cannot accumulate endless menu
  entries. The frontend no longer offers the retired New Challenge action.

Daily IDs include a recipe version, date, and slot. The service derives deterministic seeds,
verifies candidates with the actual game rules, and caches one date's immutable set. Retries and
search size are bounded. Changing daily recipe behavior requires a new version so a published
puzzle does not silently change during its day. Keep all API replicas on the same recipe version.

## How the code fits together

`NumberForge.Core` owns arithmetic, card consumption, Echo, game completion, generation, and
solution analysis. A shared pure transition helper keeps the solver and real gameplay consistent.
Checked arithmetic rejects overflow without changing a number, consuming a card, or spending Echo.

`NumberForge.Api` authenticates an anonymous player, validates a request, loads the owned game,
asks Core to apply a card, and commits the new revision. Its response is a snapshot. Persisted
history contains original operation IDs and is replayed through Core when restored. Only explicit
add/multiply card types can be deserialized; storage does not instantiate arbitrary CLR types.

`IGameStateRepository` separates the game service from storage. SQL Server uses pooled EF Core
contexts, indexed owner/expiry lookups, and an atomic revision comparison. A winning attempt and
its route discovery commit together. Development may explicitly fall back to bounded memory
storage; that mode loses attempts and discoveries at process restart and logs a warning.
Production refuses to start without a SQL connection. See [SQL Server setup](sql-server-persistence.md).

Monotonic timing is used within the process that created an attempt. After restart or on another
replica, timing falls back to the persisted UTC start; keep server clocks synchronized. Time is
informational, so clock changes cannot affect a win or its grade.

## HTTP contract

All `/api` requests use the protected guest cookie. Same-origin browser fetches send it automatically.
For scripts, keep a cookie jar and send an allowed `Origin` on writes.

- `GET /api/puzzles`: the four practice definitions plus today's three definitions.
- `GET /api/puzzles/daily`: `date`, `availableFromUtc`, `expiresAtUtc`, and the three daily puzzles.
- `GET /api/puzzles/{puzzleId}`: a currently available definition, with route count concealed until a win.
- `POST /api/sessions?puzzleId=...`: creates an owned attempt and returns HTTP 201 with its location.
- `GET /api/sessions/{sessionId}`: restores only that player's unexpired attempt.
- `POST /api/sessions/{sessionId}/moves`: accepts a card ID, optional expected revision, and optional
  request ID. It never accepts a current number, score, duration, or claimed discovery.
- `GET /api/puzzles/{puzzleId}/progress`: verified discoveries for that player and current puzzle,
  including `hasSolved`, nullable `totalRoutes`, `foundRoutes`, `bestMoves`, and discovered route IDs.

Example move body:

```json
{
  "operationId": 2,
  "expectedRevision": 1,
  "requestId": "4880b047-279f-4e55-a9fb-79b9ce953355"
}
```

Keep the same `requestId` when retrying that exact move after a connection failure. A repeated
accepted request returns the latest saved state without spending another card, including when
it races across two API instances. A mismatched revision or reused card returns 409; reload the
attempt before choosing another move. Unknown JSON fields are rejected rather than ignored.
Session responses add `revision`, `status` (`active`, `won`, `lost`, or `expired`), `playUntilUtc`,
and `expiresAtUtc`. Existing browser callers can omit the two new request fields.

An unknown session and somebody else's session both return 404. Daily expiry returns 410;
rate/admission limits return 429, and temporary database failures return a generic 503. Errors
never include database credentials or stack traces.

## Security and operating limits

The API uses an authenticated, HttpOnly, SameSite Strict guest cookie. Outside Development it is
Secure and uses the `__Host-` prefix. Guest credentials expire after 30 days. Clearing the cookie
or changing devices creates a different player; account recovery and cross-device identity are
not implemented. A session GUID alone is not a credential.

Unsafe requests require an allowed exact Origin or same-origin browser metadata. Cross-site
requests are rejected before a cookie can be issued. This is a same-origin application: host the
web app and `/api` under one public origin. Cookies are automatically attached by browsers, which
is why these CSRF protections accompany authentication.
See [Microsoft's CSRF guidance](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0).

Defaults per API process: 4 KiB request bodies, 240 requests/minute/IP, 120 writes/minute/IP,
20 attempt creations/minute/IP, 6,000 requests/minute overall, 100 concurrent requests, and
8,192 tracked IP buckets. Forwarded client addresses are accepted only from configured proxies.
Shared NATs use one IP budget; tune these limits using observed traffic. Enforce traffic limits
at the public proxy as well, especially when running multiple API instances.

Default storage admission allows 200 retained attempts per player and 50,000 overall. Practice
attempts expire after 24 hours; practice discoveries last 30 days. Daily attempts/discoveries are
retained through the day after their puzzle date, while collection endpoints offer only today's
puzzles. A worker removes expired rows in bounded batches; expiry is enforced on reads immediately.
Limits are configurable under `ApiSecurity` and `SessionLimits` and validated at startup.

These protections address unauthorized access, forged state, repeated/racing moves, arithmetic
wrapping, cross-site requests, and several resource-exhaustion paths. They do not prevent someone
from solving a public math puzzle with a script, and they do not replace host patching, backups,
TLS termination, monitoring, or protection against distributed denial of service.

## Running and deploying

Local development uses the existing frontend proxy and API port 5100:

```sh
dotnet run --project NumberForge.Api --launch-profile http
dotnet test NumberForge.Tests/NumberForge.Tests.csproj -c Release
```

On this Mac, the installed executable is `/usr/local/share/dotnet/dotnet` if `dotnet` is not on PATH.
A restart in development memory mode clears saved attempts; the frontend's missing-session flow
can then start a fresh attempt.

Before a production deployment, configure:

1. `ConnectionStrings__NumberForge` through secret configuration, apply the numbered SQL schema,
   and run the real SQL integration tests described in the persistence guide.
2. `AllowedHosts` with explicit hostnames and `ApiSecurity__AllowedOrigins__0` with the public
   HTTPS origin. Wildcards, missing origins, and insecure production origins fail startup.
3. `ApiSecurity__DataProtectionKeyPath` pointing to protected persistent storage shared by replicas.
   Restrict filesystem access to the application identity. Use the certificate path/password
   settings or a deployment-specific key protector to encrypt keys at rest. Explicit file
   persistence alone does not provide that encryption. See
   [Microsoft's Data Protection guidance](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).
4. `ApiSecurity__KnownProxies__0` only for the actual reverse proxy address when forwarding HTTPS
   and client IP information. Keep the API inaccessible around that proxy. Leave
   `ASPNETCORE_FORWARDEDHEADERS_ENABLED` unset: production rejects that automatic trust shortcut.
5. Database backups and restore checks, secret rotation, logs/alerts, dependency updates, and
   representative load tests against the intended hosting environment.

The SQL revision checks follow the same conditional-update principle described in
[EF Core's concurrency guidance](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).
The ordinary suite and HTTP tests run locally without a database. The real SQL tests explicitly
skip when no dedicated test database is configured; this is still a prelaunch verification item.
No infrastructure has been provisioned or deployed as part of this change.

## Verification performed for this change

The Release suite passed 221 tests, with three real SQL Server tests explicitly skipped because
no dedicated database was configured. The console project also builds without warnings.
A live Kestrel smoke test played and won all three current daily puzzles, verified safe retry,
ownership, discovery credit, malformed-state rejection, and the existing Vite proxy connection.
The backend pass left all 29 frontend source/configuration hashes unchanged. A subsequent
frontend integration pass updated gameplay controls and API contracts while preserving both CSS files.
The API and test projects' transitive dependency audits reported no known vulnerable packages.

A Release solver microbenchmark over 300 analyses of a six-card/five-move Echo puzzle improved
from a median 158.96 ms to 10.79 ms, with allocations falling from 304,473,640 to 55,240 bytes.
This measures solver work on this machine, not production HTTP throughput. Independent replay
checked all 1,095 puzzles across 365 sampled daily sets: every set generated successfully and
its reported minimum/route count matched legal play. Production database load/transaction tests
and host configuration remain deployment verification tasks.

Frontend integration verification: 31 frontend tests pass, the production bundle builds, and lint
reports no warnings. A headless Chrome playthrough completed all three daily slots and Echo practice,
restored an attempt after reloading, and replayed a winning route without increasing its discovery count.
Both frontend CSS files match their hashes from before the integration pass. Open the existing Vite
server at `http://localhost:5173` to play the Today or Practice groups in the current layout.
