CREATE PROCEDURE dbo.BackfillLastNCodeGroupBatch
    @ResourceTypeId SMALLINT,
    @SearchParamId SMALLINT,
    @Generation BIGINT,
    @StartResourceSurrogateId BIGINT,
    @EndResourceSurrogateId BIGINT
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

        SELECT @SnapshotHighWaterSurrogateId = SnapshotHighWaterSurrogateId
        FROM dbo.LastNCodeGroupGeneration WITH (UPDLOCK, HOLDLOCK)
        WHERE ResourceTypeId = @ResourceTypeId
            AND SearchParamId = @SearchParamId
            AND Generation = @Generation
            AND State = 'Building';

        IF @@ROWCOUNT = 0
            THROW 50424, 'LastN code-group generation is not active.', 1;

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

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
