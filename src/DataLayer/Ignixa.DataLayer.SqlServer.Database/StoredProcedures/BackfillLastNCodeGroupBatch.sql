CREATE PROCEDURE dbo.BackfillLastNCodeGroupBatch
    @ResourceTypeId SMALLINT,
    @SearchParamId SMALLINT,
    @Generation BIGINT,
    @AttemptId UNIQUEIDENTIFIER,
    @StartResourceSurrogateId BIGINT,
    @EndResourceSurrogateId BIGINT,
    @LeaseExpiresDateTime DATETIME2(7)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @EndResourceSurrogateId < @StartResourceSurrogateId
        THROW 50423, 'LastN backfill batch range is invalid.', 1;

    DECLARE @LockResult INT;
    DECLARE @LockResource NVARCHAR(255) =
        CONCAT('LastNCodeGroup:', @ResourceTypeId, ':', @SearchParamId);
    DECLARE @SnapshotHighWaterSurrogateId BIGINT;
    DECLARE @LastCommittedResourceSurrogateId BIGINT;
    DECLARE @CommittedEndResourceSurrogateId BIGINT;
    DECLARE @Resources dbo.LastNResourceScopeList;

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
            @SnapshotHighWaterSurrogateId = SnapshotHighWaterSurrogateId,
            @LastCommittedResourceSurrogateId = LastCommittedResourceSurrogateId
        FROM dbo.LastNCodeGroupGeneration WITH (UPDLOCK, HOLDLOCK)
        WHERE ResourceTypeId = @ResourceTypeId
            AND SearchParamId = @SearchParamId
            AND Generation = @Generation
            AND AttemptId = @AttemptId
            AND State = 'Building';

        IF @@ROWCOUNT = 0
            THROW 50424, 'LastN code-group generation is not active.', 1;

        IF @SnapshotHighWaterSurrogateId IS NULL
            THROW 50423, 'LastN backfill batch range is invalid.', 1;

        SET @CommittedEndResourceSurrogateId =
            CASE
                WHEN @EndResourceSurrogateId > @SnapshotHighWaterSurrogateId
                    THEN @SnapshotHighWaterSurrogateId
                ELSE @EndResourceSurrogateId
            END;

        IF @LastCommittedResourceSurrogateId IS NOT NULL
            AND @CommittedEndResourceSurrogateId <= @LastCommittedResourceSurrogateId
        BEGIN
            UPDATE dbo.LastNCodeGroupGeneration
            SET LeaseExpiresDateTime = @LeaseExpiresDateTime
            WHERE ResourceTypeId = @ResourceTypeId
                AND SearchParamId = @SearchParamId
                AND Generation = @Generation
                AND AttemptId = @AttemptId
                AND State = 'Building';

            COMMIT TRANSACTION;
            RETURN;
        END;

        IF @LastCommittedResourceSurrogateId IS NOT NULL
            AND @StartResourceSurrogateId <> @LastCommittedResourceSurrogateId + 1
            THROW 50428, 'LastN backfill batch does not start at the first uncommitted surrogate id.', 1;

        INSERT INTO @Resources (ResourceTypeId, SearchParamId, ResourceSurrogateId)
        SELECT @ResourceTypeId, @SearchParamId, resource.ResourceSurrogateId
        FROM dbo.Resource AS resource
        WHERE resource.ResourceTypeId = @ResourceTypeId
            AND resource.IsHistory = 0
            AND resource.IsDeleted = 0
            AND resource.ResourceSurrogateId >= @StartResourceSurrogateId
            AND resource.ResourceSurrogateId <= @EndResourceSurrogateId
            AND resource.ResourceSurrogateId <= @SnapshotHighWaterSurrogateId;

        EXEC dbo.MaintainLastNCodeGroups
            @Mode = 'Remove',
            @Resources = @Resources;
        EXEC dbo.MaintainLastNCodeGroups
            @Mode = 'Add',
            @Resources = @Resources;

        UPDATE dbo.LastNCodeGroupGeneration
        SET LastCommittedResourceSurrogateId = @CommittedEndResourceSurrogateId,
            LeaseExpiresDateTime = @LeaseExpiresDateTime
        WHERE ResourceTypeId = @ResourceTypeId
            AND SearchParamId = @SearchParamId
            AND Generation = @Generation
            AND AttemptId = @AttemptId
            AND State = 'Building';

        IF @@ROWCOUNT = 0
            THROW 50424, 'LastN code-group generation is not active.', 1;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
