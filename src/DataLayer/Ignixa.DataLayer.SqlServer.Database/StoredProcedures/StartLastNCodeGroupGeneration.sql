CREATE PROCEDURE dbo.StartLastNCodeGroupGeneration
    @ResourceTypeId SMALLINT,
    @SearchParamId SMALLINT,
    @StartedGeneration BIGINT = NULL OUTPUT,
    @StartedState VARCHAR(16) = NULL OUTPUT,
    @StartedSnapshotHighWaterSurrogateId BIGINT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @LockResult INT;
    DECLARE @LockResource NVARCHAR(255) =
        CONCAT('LastNCodeGroup:', @ResourceTypeId, ':', @SearchParamId);
    DECLARE @SnapshotHighWaterSurrogateId BIGINT;

    BEGIN TRY
        BEGIN TRANSACTION;

        EXEC @LockResult = sys.sp_getapplock
            @Resource = @LockResource,
            @LockMode = 'Exclusive',
            @LockOwner = 'Transaction',
            @LockTimeout = 15000;
        IF @LockResult < 0
            THROW 50410, 'Unable to acquire LastN code-group scope lock.', 1;

        IF NOT EXISTS (
            SELECT 1
            FROM dbo.LastNCodeGroupGeneration WITH (UPDLOCK, HOLDLOCK)
            WHERE ResourceTypeId = @ResourceTypeId
                AND SearchParamId = @SearchParamId)
        BEGIN
            THROW 50422, 'LastN code-group scope is not enabled.', 1;
        END;

        SELECT @SnapshotHighWaterSurrogateId = MAX(ResourceSurrogateId)
        FROM dbo.Resource
        WHERE ResourceTypeId = @ResourceTypeId
            AND IsHistory = 0
            AND IsDeleted = 0;

        UPDATE dbo.LastNCodeGroupGeneration
        SET Generation = Generation + 1,
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

        SELECT Generation, State, SnapshotHighWaterSurrogateId
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
