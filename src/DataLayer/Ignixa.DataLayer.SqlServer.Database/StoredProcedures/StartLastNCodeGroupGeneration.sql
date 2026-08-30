CREATE PROCEDURE dbo.StartLastNCodeGroupGeneration
    @ResourceTypeId SMALLINT,
    @SearchParamId SMALLINT,
    @AttemptId UNIQUEIDENTIFIER,
    @CurrentDateTime DATETIME2(7),
    @LeaseExpiresDateTime DATETIME2(7),
    @StartedGeneration BIGINT = NULL OUTPUT,
    @StartedState VARCHAR(16) = NULL OUTPUT,
    @StartedSnapshotHighWaterSurrogateId BIGINT = NULL OUTPUT,
    @StartedLastCommittedResourceSurrogateId BIGINT = NULL OUTPUT,
    @StartedLeaseExpiresDateTime DATETIME2(7) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @AttemptId IS NULL
        THROW 50426, 'LastN code-group generation attempt id is required.', 1;

    IF @CurrentDateTime IS NULL
        OR @LeaseExpiresDateTime IS NULL
        OR @LeaseExpiresDateTime <= @CurrentDateTime
        THROW 50427, 'LastN code-group generation lease is invalid.', 1;

    DECLARE @LockResult INT;
    DECLARE @LockResource NVARCHAR(255) =
        CONCAT('LastNCodeGroup:', @ResourceTypeId, ':', @SearchParamId);
    DECLARE @SnapshotHighWaterSurrogateId BIGINT;
    DECLARE @CurrentAttemptId UNIQUEIDENTIFIER;
    DECLARE @CurrentState VARCHAR(16);
    DECLARE @CurrentLeaseExpiresDateTime DATETIME2(7);

    BEGIN TRY
        BEGIN TRANSACTION;

        EXEC @LockResult = sys.sp_getapplock
            @Resource = @LockResource,
            @LockMode = 'Exclusive',
            @LockOwner = 'Transaction',
            @LockTimeout = 15000;
        IF @LockResult < 0
            THROW 50410, 'Unable to acquire LastN code-group scope lock.', 1;

        SELECT
            @CurrentAttemptId = AttemptId,
            @CurrentState = State,
            @CurrentLeaseExpiresDateTime = LeaseExpiresDateTime
        FROM dbo.LastNCodeGroupGeneration WITH (UPDLOCK, HOLDLOCK)
        WHERE ResourceTypeId = @ResourceTypeId
            AND SearchParamId = @SearchParamId;

        IF @@ROWCOUNT = 0
        BEGIN
            THROW 50422, 'LastN code-group scope is not enabled.', 1;
        END;

        IF @CurrentState = 'Building'
        BEGIN
            IF @CurrentAttemptId <> @AttemptId
                AND @CurrentLeaseExpiresDateTime > @CurrentDateTime
                THROW 50425, 'A different LastN code-group generation attempt is active.', 1;

            UPDATE dbo.LastNCodeGroupGeneration
            SET AttemptId = @AttemptId,
                LeaseExpiresDateTime = @LeaseExpiresDateTime
            WHERE ResourceTypeId = @ResourceTypeId
                AND SearchParamId = @SearchParamId;

            SELECT
                @StartedGeneration = Generation,
                @StartedState = State,
                @StartedSnapshotHighWaterSurrogateId = SnapshotHighWaterSurrogateId,
                @StartedLastCommittedResourceSurrogateId = LastCommittedResourceSurrogateId,
                @StartedLeaseExpiresDateTime = LeaseExpiresDateTime
            FROM dbo.LastNCodeGroupGeneration
            WHERE ResourceTypeId = @ResourceTypeId
                AND SearchParamId = @SearchParamId;

            SELECT Generation, State, SnapshotHighWaterSurrogateId, AttemptId,
                   LastCommittedResourceSurrogateId, LeaseExpiresDateTime
            FROM dbo.LastNCodeGroupGeneration
            WHERE ResourceTypeId = @ResourceTypeId
                AND SearchParamId = @SearchParamId;

            COMMIT TRANSACTION;
            RETURN;
        END;

        SELECT @SnapshotHighWaterSurrogateId = MAX(ResourceSurrogateId)
        FROM dbo.Resource
        WHERE ResourceTypeId = @ResourceTypeId
            AND IsHistory = 0
            AND IsDeleted = 0;

        UPDATE dbo.LastNCodeGroupGeneration
        SET Generation = Generation + 1,
            AttemptId = @AttemptId,
            State = 'Building',
            SnapshotHighWaterSurrogateId = @SnapshotHighWaterSurrogateId,
            LastCommittedResourceSurrogateId = NULL,
            LeaseExpiresDateTime = @LeaseExpiresDateTime,
            StartedDateTime = @CurrentDateTime,
            CompletedDateTime = NULL,
            FailureReason = NULL
        WHERE ResourceTypeId = @ResourceTypeId
            AND SearchParamId = @SearchParamId;

        SELECT
            @StartedGeneration = Generation,
            @StartedState = State,
            @StartedSnapshotHighWaterSurrogateId = SnapshotHighWaterSurrogateId,
            @StartedLastCommittedResourceSurrogateId = LastCommittedResourceSurrogateId,
            @StartedLeaseExpiresDateTime = LeaseExpiresDateTime
        FROM dbo.LastNCodeGroupGeneration
        WHERE ResourceTypeId = @ResourceTypeId
            AND SearchParamId = @SearchParamId;

        SELECT Generation, State, SnapshotHighWaterSurrogateId, AttemptId,
               LastCommittedResourceSurrogateId, LeaseExpiresDateTime
        FROM dbo.LastNCodeGroupGeneration
        WHERE ResourceTypeId = @ResourceTypeId
            AND SearchParamId = @SearchParamId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
