CREATE PROCEDURE dbo.StartLastNCodeGroupGeneration
    @ResourceTypeId SMALLINT,
    @SearchParamId SMALLINT,
    @AttemptId UNIQUEIDENTIFIER,
    @StartedGeneration BIGINT = NULL OUTPUT,
    @StartedState VARCHAR(16) = NULL OUTPUT,
    @StartedSnapshotHighWaterSurrogateId BIGINT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @AttemptId IS NULL
        THROW 50426, 'LastN code-group generation attempt id is required.', 1;

    DECLARE @LockResult INT;
    DECLARE @LockResource NVARCHAR(255) =
        CONCAT('LastNCodeGroup:', @ResourceTypeId, ':', @SearchParamId);
    DECLARE @SnapshotHighWaterSurrogateId BIGINT;
    DECLARE @CurrentAttemptId UNIQUEIDENTIFIER;
    DECLARE @CurrentState VARCHAR(16);

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
            @CurrentState = State
        FROM dbo.LastNCodeGroupGeneration WITH (UPDLOCK, HOLDLOCK)
        WHERE ResourceTypeId = @ResourceTypeId
            AND SearchParamId = @SearchParamId;

        IF @@ROWCOUNT = 0
        BEGIN
            THROW 50422, 'LastN code-group scope is not enabled.', 1;
        END;

        IF @CurrentAttemptId = @AttemptId
        BEGIN
            SELECT
                @StartedGeneration = Generation,
                @StartedState = State,
                @StartedSnapshotHighWaterSurrogateId = SnapshotHighWaterSurrogateId
            FROM dbo.LastNCodeGroupGeneration
            WHERE ResourceTypeId = @ResourceTypeId
                AND SearchParamId = @SearchParamId;

            SELECT Generation, State, SnapshotHighWaterSurrogateId, AttemptId
            FROM dbo.LastNCodeGroupGeneration
            WHERE ResourceTypeId = @ResourceTypeId
                AND SearchParamId = @SearchParamId;

            COMMIT TRANSACTION;
            RETURN;
        END;

        IF @CurrentState = 'Building'
            THROW 50425, 'A different LastN code-group generation attempt is active.', 1;

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
            StartedDateTime = SYSUTCDATETIME(),
            CompletedDateTime = NULL,
            FailureReason = NULL
        WHERE ResourceTypeId = @ResourceTypeId
            AND SearchParamId = @SearchParamId;

        SELECT
            @StartedGeneration = Generation,
            @StartedState = State,
            @StartedSnapshotHighWaterSurrogateId = SnapshotHighWaterSurrogateId
        FROM dbo.LastNCodeGroupGeneration
        WHERE ResourceTypeId = @ResourceTypeId
            AND SearchParamId = @SearchParamId;

        SELECT Generation, State, SnapshotHighWaterSurrogateId, AttemptId
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
