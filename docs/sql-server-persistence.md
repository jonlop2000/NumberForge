# SQL Server persistence

The API stores attempts and discovered routes in SQL Server through `IGameStateRepository`.
`GameSessionStore` still owns the game rules and replay validation. The repository stores only
the server-created session payload; an HTTP client cannot supply a database row or overwrite
a saved score.

`SqlServerGameStateRepository` creates a separate pooled Entity Framework context for each
operation. Reads filter by both player identity and expiry. A move updates one expected
revision, so two simultaneous requests cannot both spend the same turn. The session update
and a newly discovered winning route commit in one transaction. The route table's composite
primary key prevents duplicate discoveries for the same player and puzzle collection.

## Configure an environment

Use a SQL Server database or Azure SQL database with a certificate trusted by the API host.
Set the `ConnectionStrings:NumberForge` configuration value through the deployment platform's
secret store; the equivalent environment variable is `ConnectionStrings__NumberForge`.
The registration method is `services.AddSqlServerGameState(connectionString)`.

The connection must name both a server and a database. Encryption is required and
`TrustServerCertificate=true` is rejected. Prefer managed identity or another deployment-managed
credential instead of adding a password to checked-in configuration. Local development can use
the application's explicitly configured memory repository when a SQL Server is unavailable;
memory state does not survive a restart and does not coordinate multiple API instances.

Apply [the initial schema script](../NumberForge.Api/Persistence/Schema/001_game_state.sql)
to the intended database with a deployment identity before starting the API. It creates the
`numberforge.Sessions` and `numberforge.Routes` tables, validation constraints, and indexes.
The script is idempotent: applying it twice does not delete or duplicate existing tables.
Future changes require a new versioned schema script. The API deliberately does not create
databases or change schemas on startup.

The runtime database identity needs `SELECT`, `INSERT`, `UPDATE`, and `DELETE` on the
`numberforge` schema. It does not need schema alteration rights. SQL Server application locks
use the current database and transaction, so every API replica must use the same database.
Keep cookie data-protection keys shared and persistent across replicas as documented by the
API security configuration; database persistence alone cannot preserve an encrypted cookie
when the keys used to decrypt it disappear.

## Resource and concurrency limits

- Session admission takes a short transaction-owned application lock while checking quotas and
  inserting. This enforces both global and per-player limits across API instances.
- Ordinary moves use an atomic revision comparison. A winning move additionally locks just
  that player's puzzle collection while deduplicating the route.
- Lock acquisition waits at most five seconds. SQL commands have a fifteen-second timeout,
  and request cancellation is passed to database calls.
- Payloads are limited to 65,536 UTF-16 characters and SQL Server verifies that they contain JSON.
  Identifiers have bounded lengths and use binary collation for exact owner/key comparisons.
- Each cleanup call deletes at most 500 expired sessions and 500 expired routes. Expired data
  is excluded from reads immediately, even before physical cleanup runs.
- Connections do not enable automatic transaction retries. A connection failure during commit
  may leave its outcome uncertain; the API's saved move receipt and revision handling allow a
  client to recover state without blindly replaying the transaction.

## Verification before deployment

The ordinary test suite validates the SQL model, generated schema, secure registration, and
input guards without contacting a database. Those checks do not prove real transaction behavior.

Three integration tests use **real SQL Server** when `NUMBERFORGE_TEST_SQLSERVER` contains a
connection string for a dedicated database whose name ends in `_tests`. Create that database
outside the test suite and use a test identity allowed to initialize its schema. The tests
apply the initial schema twice, exercise independent repository instances, and remove their
own data afterward. Never point this setting at a live player database.

```sh
dotnet test NumberForge.Tests/NumberForge.Tests.csproj --filter FullyQualifiedName~SqlServerRepositoryIntegrationTests
```

The integration cases cover concurrent per-player admission, ownership isolation, competing
move revisions, atomic win/discovery persistence, duplicate routes, immutable timestamps,
expiry, and bounded cleanup. Without the test connection, they report **skipped** instead of
silently substituting a different database engine. Run them against the deployment's SQL Server
version, and confirm backups and restoration before launching with persistent player data.

The API references EF Core SQL Server 10.0.14. Its SQL client dependency originally selected
`System.Security.Cryptography.Pkcs` 9.0.11; an explicit 10.0.14 reference removes the vulnerable
transitive version. Re-run `dotnet list NumberForge.Api/NumberForge.Api.csproj package --include-transitive --vulnerable`
when dependencies change. Microsoft's advisories document the fixed versions:
[CVE-2026-70162](https://github.com/advisories/GHSA-2fmv-rv2c-59x7) and
[CVE-2026-84008](https://github.com/advisories/GHSA-846f-f764-76mx).
