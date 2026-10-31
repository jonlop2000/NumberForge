-- Run against the intended NumberForge database before starting the API.
-- This initializes the schema idempotently; later schema changes need their own numbered script.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @schemaLock int;
EXEC @schemaLock = sys.sp_getapplock @Resource = 'numberforge:schema',
    @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
IF @schemaLock < 0 THROW 50000, 'Could not acquire NumberForge schema lock.', 1;

IF SCHEMA_ID('numberforge') IS NULL EXEC('CREATE SCHEMA [numberforge]');

IF OBJECT_ID('[numberforge].[Sessions]', 'U') IS NULL
BEGIN
    CREATE TABLE [numberforge].[Sessions]
    (
        [Id] uniqueidentifier NOT NULL,
        [PlayerId] varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [StartedAtUtc] datetimeoffset NOT NULL,
        [ExpiresAtUtc] datetimeoffset NOT NULL,
        [PlayUntilUtc] datetimeoffset NULL,
        [Revision] int NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Sessions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Sessions_Revision] CHECK ([Revision] >= 0),
        CONSTRAINT [CK_Sessions_Lifetime] CHECK ([ExpiresAtUtc] > [StartedAtUtc]),
        CONSTRAINT [CK_Sessions_PlayWindow] CHECK
            ([PlayUntilUtc] IS NULL OR ([PlayUntilUtc] > [StartedAtUtc] AND [PlayUntilUtc] <= [ExpiresAtUtc])),
        CONSTRAINT [CK_Sessions_Payload] CHECK (ISJSON([Payload]) = 1 AND DATALENGTH([Payload]) <= 131072)
    );
    CREATE INDEX [IX_Sessions_PlayerId_ExpiresAtUtc] ON [numberforge].[Sessions] ([PlayerId], [ExpiresAtUtc]);
    CREATE INDEX [IX_Sessions_ExpiresAtUtc] ON [numberforge].[Sessions] ([ExpiresAtUtc]);
END;

IF OBJECT_ID('[numberforge].[Routes]', 'U') IS NULL
BEGIN
    CREATE TABLE [numberforge].[Routes]
    (
        [PlayerId] varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [CollectionKey] varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [RouteId] varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [MoveCount] int NOT NULL,
        [FoundAtUtc] datetimeoffset NOT NULL,
        [ExpiresAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Routes] PRIMARY KEY ([PlayerId], [CollectionKey], [RouteId]),
        CONSTRAINT [CK_Routes_MoveCount] CHECK ([MoveCount] > 0),
        CONSTRAINT [CK_Routes_Lifetime] CHECK ([ExpiresAtUtc] > [FoundAtUtc])
    );
    CREATE INDEX [IX_Routes_ExpiresAtUtc] ON [numberforge].[Routes] ([ExpiresAtUtc]);
END;

COMMIT TRANSACTION;
