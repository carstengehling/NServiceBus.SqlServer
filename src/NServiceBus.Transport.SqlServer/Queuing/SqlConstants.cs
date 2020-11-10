namespace NServiceBus.Transport.SqlServer
{
    static class SqlConstants
    {
        public static readonly string PurgeText = "DELETE FROM {0}";

        public static readonly string SendText =
            @"
DECLARE @NOCOUNT VARCHAR(3) = 'OFF';
IF ( (512 & @@OPTIONS) = 512 ) SET @NOCOUNT = 'ON'
SET NOCOUNT ON;

INSERT INTO {0} (
    Id,
    CorrelationId,
    ReplyToAddress,
    Recoverable,
    Expires,
    Headers,
    Body)
VALUES (
    @Id,
    @CorrelationId,
    @ReplyToAddress,
    1,
    CASE WHEN @TimeToBeReceivedMs IS NOT NULL
        THEN DATEADD(ms, @TimeToBeReceivedMs, GETUTCDATE()) END,
    @Headers,
    @Body);

IF (@NOCOUNT = 'ON') SET NOCOUNT ON;
IF (@NOCOUNT = 'OFF') SET NOCOUNT OFF;";

        public static readonly string StoreDelayedMessageText =
@"
DECLARE @NOCOUNT VARCHAR(3) = 'OFF';
IF ( (512 & @@OPTIONS) = 512 ) SET @NOCOUNT = 'ON'
SET NOCOUNT ON;

DECLARE @DueAfter DATETIME = GETUTCDATE();
SET @DueAfter = DATEADD(ms, @DueAfterMilliseconds, @DueAfter);
SET @DueAfter = DATEADD(s, @DueAfterSeconds, @DueAfter);
SET @DueAfter = DATEADD(n, @DueAfterMinutes, @DueAfter);
SET @DueAfter = DATEADD(hh, @DueAfterHours, @DueAfter);
SET @DueAfter = DATEADD(d, @DueAfterDays, @DueAfter);

INSERT INTO {0} (
    Headers,
    Body,
    Due)
VALUES (
    @Headers,
    @Body,
    @DueAfter);

IF(@NOCOUNT = 'ON') SET NOCOUNT ON;
IF(@NOCOUNT = 'OFF') SET NOCOUNT OFF;";

        public static readonly string TryDeleteLeasedRow = @"
DECLARE @NOCOUNT VARCHAR(3) = 'OFF';
IF ( (512 & @@OPTIONS) = 512 ) SET @NOCOUNT = 'ON';
SET NOCOUNT OFF;

DELETE FROM {0} WHERE LeaseId = @LeaseId

IF (@NOCOUNT = 'ON') SET NOCOUNT ON;
IF (@NOCOUNT = 'OFF') SET NOCOUNT OFF;
";

        public static readonly string ReleaseLease = @"
DECLARE @NOCOUNT VARCHAR(3) = 'OFF';
IF ( (512 & @@OPTIONS) = 512 ) SET @NOCOUNT = 'ON';
SET NOCOUNT ON;

UPDATE {0}
SET LeaseExpiration = NULL,
    LeaseId = NULL
WHERE LeaseId = @LeaseId;

IF (@NOCOUNT = 'ON') SET NOCOUNT ON;
IF (@NOCOUNT = 'OFF') SET NOCOUNT OFF;
";

        public static readonly string LeaseBasedReceiveText = @"
DECLARE @NOCOUNT VARCHAR(3) = 'OFF';
IF ( (512 & @@OPTIONS) = 512 ) SET @NOCOUNT = 'ON';
SET NOCOUNT ON;

UPDATE {0} WITH (READPAST)
SET LeaseExpiration = GETUTCDATE() + '00:00:30',
    LeaseId = NEWID(),
    DequeueCount = DequeueCount+1 
OUTPUT
	deleted.Id,
    deleted.CorrelationId,
    deleted.ReplyToAddress,
    CASE WHEN deleted.Expires IS NULL
        THEN 0
        ELSE CASE WHEN deleted.Expires > GETUTCDATE()
            THEN 0
            ELSE 1
        END
    END,
    deleted.Headers,
    deleted.Body,
	inserted.LeaseId
WHERE [RowVersion] = (SELECT TOP (1) [RowVersion] FROM {0} WHERE [LeaseExpiration] IS NULL OR [LeaseExpiration] < GETUTCDATE());

IF (@NOCOUNT = 'ON') SET NOCOUNT ON;
IF (@NOCOUNT = 'OFF') SET NOCOUNT OFF;";


        public static readonly string ReceiveText = @"
DECLARE @NOCOUNT VARCHAR(3) = 'OFF';
IF ( (512 & @@OPTIONS) = 512 ) SET @NOCOUNT = 'ON';
SET NOCOUNT ON;

WITH message AS (
    SELECT TOP(1) *
    FROM {0} WITH (UPDLOCK, READPAST, ROWLOCK)
    ORDER BY RowVersion)
DELETE FROM message
OUTPUT
    deleted.Id,
    deleted.CorrelationId,
    deleted.ReplyToAddress,
    CASE WHEN deleted.Expires IS NULL
        THEN 0
        ELSE CASE WHEN deleted.Expires > GETUTCDATE()
            THEN 0
            ELSE 1
        END
    END,
    deleted.Headers,
    deleted.Body;

IF (@NOCOUNT = 'ON') SET NOCOUNT ON;
IF (@NOCOUNT = 'OFF') SET NOCOUNT OFF;";

        public static readonly string MoveDueDelayedMessageText = @"
DECLARE @NOCOUNT VARCHAR(3) = 'OFF';
IF ( (512 & @@OPTIONS) = 512 ) SET @NOCOUNT = 'ON';
SET NOCOUNT ON;

WITH message AS (
    SELECT TOP(@BatchSize) *
    FROM {0} WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE Due < GETUTCDATE())
DELETE FROM message
OUTPUT
    NEWID(),
    NULL,
    NULL,
    1,
    NULL,
    deleted.Headers,
    deleted.Body
INTO {1};

IF (@NOCOUNT = 'ON') SET NOCOUNT ON;
IF (@NOCOUNT = 'OFF') SET NOCOUNT OFF;";

        public static readonly string LeaseBasedMoveDueDelayedMessageText = @"
DECLARE @NOCOUNT VARCHAR(3) = 'OFF';
IF ( (512 & @@OPTIONS) = 512 ) SET @NOCOUNT = 'ON';
SET NOCOUNT ON;

WITH message AS (
    SELECT TOP(@BatchSize) *
    FROM {0} WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE Due < GETUTCDATE())
DELETE FROM message
OUTPUT
    NEWID(),
    NULL,
    NULL,
    1,
    NULL,
    deleted.Headers,
    deleted.Body,
    NULL,
    NULL,
    NULL
INTO {1};

IF (@NOCOUNT = 'ON') SET NOCOUNT ON;
IF (@NOCOUNT = 'OFF') SET NOCOUNT OFF;";

        public static readonly string PeekText = @"
SELECT isnull(cast(max([RowVersion]) - min([RowVersion]) + 1 AS int), 0) Id FROM {0} WITH (nolock)";

        public static readonly string LeaseBasedPeekText = @"
SELECT isnull(cast(max([RowVersion]) - min([RowVersion]) + 1 AS int), 0) Id FROM {0} WITH (nolock) WHERE [LeaseExpiration] IS NULL OR [LeaseExpiration] < GETUTCDATE()  ";


        public static readonly string AddMessageBodyStringColumn = @"
IF NOT EXISTS (
    SELECT *
    FROM {1}.sys.objects
    WHERE object_id = OBJECT_ID(N'{0}')
        AND type in (N'U'))
RETURN

IF EXISTS (
  SELECT *
  FROM   {1}.sys.columns
  WHERE  object_id = OBJECT_ID(N'{0}')
         AND name = 'BodyString'
)
RETURN

EXEC sp_getapplock @Resource = '{0}_lock', @LockMode = 'Exclusive'

IF EXISTS (
  SELECT *
  FROM   {1}.sys.columns
  WHERE  object_id = OBJECT_ID(N'{0}')
         AND name = 'BodyString'
)
BEGIN
    EXEC sp_releaseapplock @Resource = '{0}_lock'
    RETURN
END

ALTER TABLE {0} 
ADD BodyString as cast(Body as nvarchar(max));

EXEC sp_releaseapplock @Resource = '{0}_lock'";

        public static readonly string LeaseBasedCreateQueueText = @"
IF EXISTS (
    SELECT *
    FROM {1}.sys.objects
    WHERE object_id = OBJECT_ID(N'{0}')
        AND type in (N'U'))
RETURN

EXEC sp_getapplock @Resource = '{0}_lock', @LockMode = 'Exclusive'

IF EXISTS (
    SELECT *
    FROM {1}.sys.objects
    WHERE object_id = OBJECT_ID(N'{0}')
        AND type in (N'U'))
BEGIN
    EXEC sp_releaseapplock @Resource = '{0}_lock'
    RETURN
END

CREATE TABLE {0} (
    Id uniqueidentifier NOT NULL,
    CorrelationId varchar(255),
    ReplyToAddress varchar(255),
    Recoverable bit NOT NULL,
    Expires datetime,
    Headers nvarchar(max) NOT NULL,
    Body varbinary(max),
	LeaseExpiration datetime,
	LeaseId uniqueidentifier,
	DequeueCount int,
    RowVersion bigint IDENTITY(1,1) NOT NULL
);

CREATE NONCLUSTERED INDEX Index_RowVersion ON {0}
(
	[RowVersion] ASC
)

CREATE NONCLUSTERED INDEX Index_Expires ON {0}
(
    Expires
)
INCLUDE
(
    Id,
    RowVersion
)
WHERE
    Expires IS NOT NULL

CREATE NONCLUSTERED INDEX Index_LeaseExpiration ON {0}
(
	[LeaseExpiration] ASC
)

EXEC sp_releaseapplock @Resource = '{0}_lock'
";

        public static readonly string CreateQueueText = @"
IF EXISTS (
    SELECT *
    FROM {1}.sys.objects
    WHERE object_id = OBJECT_ID(N'{0}')
        AND type in (N'U'))
RETURN

EXEC sp_getapplock @Resource = '{0}_lock', @LockMode = 'Exclusive'

IF EXISTS (
    SELECT *
    FROM {1}.sys.objects
    WHERE object_id = OBJECT_ID(N'{0}')
        AND type in (N'U'))
BEGIN
    EXEC sp_releaseapplock @Resource = '{0}_lock'
    RETURN
END

CREATE TABLE {0} (
    Id uniqueidentifier NOT NULL,
    CorrelationId varchar(255),
    ReplyToAddress varchar(255),
    Recoverable bit NOT NULL,
    Expires datetime,
    Headers nvarchar(max) NOT NULL,
    Body varbinary(max),
    RowVersion bigint IDENTITY(1,1) NOT NULL
);

CREATE NONCLUSTERED INDEX Index_RowVersion ON {0}
(
	[RowVersion] ASC
)

CREATE NONCLUSTERED INDEX Index_Expires ON {0}
(
    Expires
)
INCLUDE
(
    Id,
    RowVersion
)
WHERE
    Expires IS NOT NULL

EXEC sp_releaseapplock @Resource = '{0}_lock'";

        public static readonly string CreateDelayedMessageStoreText = @"
IF EXISTS (
    SELECT *
    FROM {1}.sys.objects
    WHERE object_id = OBJECT_ID(N'{0}')
        AND type in (N'U'))
RETURN

EXEC sp_getapplock @Resource = '{0}_lock', @LockMode = 'Exclusive'

IF EXISTS (
    SELECT *
    FROM {1}.sys.objects
    WHERE object_id = OBJECT_ID(N'{0}')
        AND type in (N'U'))
BEGIN
    EXEC sp_releaseapplock @Resource = '{0}_lock'
    RETURN
END

CREATE TABLE {0} (
    Headers nvarchar(max) NOT NULL,
    Body varbinary(max),
    Due datetime NOT NULL,
    RowVersion bigint IDENTITY(1,1) NOT NULL
);

CREATE NONCLUSTERED INDEX [Index_Due] ON {0}
(
    [Due]
)

EXEC sp_releaseapplock @Resource = '{0}_lock'";

        public static readonly string PurgeBatchOfExpiredMessagesText = @"
DELETE FROM {0}
WHERE RowVersion
    IN (SELECT TOP (@BatchSize) RowVersion
        FROM {0} WITH (READPAST)
        WHERE Expires < GETUTCDATE())";

        public static readonly string CheckIfExpiresIndexIsPresent = @"
SELECT COUNT(*)
FROM sys.indexes
WHERE name = 'Index_Expires'
    AND object_id = OBJECT_ID('{0}')";

        public static readonly string CheckIfNonClusteredRowVersionIndexIsPresent = @"
SELECT COUNT(*)
FROM sys.indexes
WHERE name = 'Index_RowVersion'
    AND object_id = OBJECT_ID('{0}')
    AND type = 2"; // 2 = non-clustered index

        public static readonly string CheckHeadersColumnType = @"
SELECT t.name
FROM sys.columns c
INNER JOIN sys.types t ON c.system_type_id = t.system_type_id
WHERE c.object_id = OBJECT_ID('{0}')
    AND c.name = 'Headers'";

        public static readonly string CreateSubscriptionTableText = @"

IF EXISTS (
    SELECT *
    FROM {1}.sys.objects
    WHERE object_id = OBJECT_ID(N'{0}')
        AND type in (N'U'))
RETURN

EXEC sp_getapplock @Resource = '{0}_lock', @LockMode = 'Exclusive'

IF EXISTS (
    SELECT *
    FROM {1}.sys.objects
    WHERE object_id = OBJECT_ID(N'{0}')
        AND type in (N'U'))
BEGIN
    EXEC sp_releaseapplock @Resource = '{0}_lock'
    RETURN
END

CREATE TABLE {0} (
    QueueAddress NVARCHAR(200) NOT NULL,
    Endpoint NVARCHAR(200),
    Topic NVARCHAR(200) NOT NULL,
    PRIMARY KEY CLUSTERED
    (
        Endpoint,
        Topic
    )
)
EXEC sp_releaseapplock @Resource = '{0}_lock'";

        public static readonly string SubscribeText = @"
MERGE {0} WITH (HOLDLOCK, TABLOCK) AS target
USING(SELECT @Endpoint AS Endpoint, @QueueAddress AS QueueAddress, @Topic AS Topic) AS source
ON target.Endpoint = source.Endpoint
AND target.Topic = source.Topic
WHEN MATCHED AND target.QueueAddress <> source.QueueAddress THEN
UPDATE SET QueueAddress = @QueueAddress
WHEN NOT MATCHED THEN
INSERT
(
    QueueAddress,
    Topic,
    Endpoint
)
VALUES
(
    @QueueAddress,
    @Topic,
    @Endpoint
);";

        public static readonly string GetSubscribersText = @"
SELECT DISTINCT QueueAddress
FROM {0}
WHERE Topic IN ({1})
";

        public static readonly string UnsubscribeText = @"
DELETE FROM {0}
WHERE
    Endpoint = @Endpoint and
    Topic = @Topic";

    }
}
