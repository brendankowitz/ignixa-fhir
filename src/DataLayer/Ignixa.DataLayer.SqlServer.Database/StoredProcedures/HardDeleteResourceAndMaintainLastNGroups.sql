CREATE PROCEDURE dbo.HardDeleteResourceAndMaintainLastNGroups
    @ResourceTypeId SMALLINT,
    @ResourceId VARCHAR(64),
    @KeepCurrentVersion BIT,
    @IsResourceChangeCaptureEnabled BIT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @AffectedLastNResources dbo.LastNResourceScopeList;
    DECLARE @AffectedLastNScopes TABLE (
        ResourceTypeId SMALLINT NOT NULL,
        SearchParamId SMALLINT NOT NULL,
        PRIMARY KEY (ResourceTypeId, SearchParamId)
    );
    DECLARE @ScopeResourceTypeId SMALLINT;
    DECLARE @SearchParamId SMALLINT;
    DECLARE @LockResult INT;
    DECLARE @LockResource NVARCHAR(255);

    IF @KeepCurrentVersion = 1 OR @KeepCurrentVersion IS NULL
    BEGIN
        EXEC dbo.HardDeleteResource
            @ResourceTypeId = @ResourceTypeId,
            @ResourceId = @ResourceId,
            @KeepCurrentVersion = @KeepCurrentVersion,
            @IsResourceChangeCaptureEnabled = @IsResourceChangeCaptureEnabled;
        RETURN;
    END;

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO @AffectedLastNScopes (ResourceTypeId, SearchParamId)
        SELECT ResourceTypeId, SearchParamId
        FROM dbo.LastNCodeGroupGeneration
        WHERE ResourceTypeId = @ResourceTypeId;

        DECLARE scope_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT ResourceTypeId, SearchParamId
        FROM @AffectedLastNScopes
        ORDER BY ResourceTypeId, SearchParamId;

        OPEN scope_cursor;
        FETCH NEXT FROM scope_cursor INTO @ScopeResourceTypeId, @SearchParamId;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @LockResource = CONCAT('LastNCodeGroup:', @ScopeResourceTypeId, ':', @SearchParamId);
            EXEC @LockResult = sys.sp_getapplock
                @Resource = @LockResource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 15000;
            IF @LockResult < 0
                THROW 50410, 'Unable to acquire LastN code-group scope lock.', 1;

            FETCH NEXT FROM scope_cursor INTO @ScopeResourceTypeId, @SearchParamId;
        END;
        CLOSE scope_cursor;
        DEALLOCATE scope_cursor;

        INSERT INTO @AffectedLastNResources (ResourceTypeId, SearchParamId, ResourceSurrogateId)
        SELECT currentResource.ResourceTypeId, affectedScope.SearchParamId,
            currentResource.ResourceSurrogateId
        FROM dbo.Resource AS currentResource
        INNER JOIN @AffectedLastNScopes AS affectedScope
            ON affectedScope.ResourceTypeId = currentResource.ResourceTypeId
        WHERE currentResource.ResourceTypeId = @ResourceTypeId
            AND currentResource.ResourceId = @ResourceId
            AND currentResource.IsHistory = 0;

        EXEC dbo.MaintainLastNCodeGroups
            @Mode = 'Remove',
            @Resources = @AffectedLastNResources;

        EXEC dbo.HardDeleteResource
            @ResourceTypeId = @ResourceTypeId,
            @ResourceId = @ResourceId,
            @KeepCurrentVersion = @KeepCurrentVersion,
            @IsResourceChangeCaptureEnabled = @IsResourceChangeCaptureEnabled;

        INSERT INTO dbo.LastNCodeGroupDirtyObservation
            (ResourceTypeId, SearchParamId, Generation, ResourceSurrogateId)
        SELECT affected.ResourceTypeId, affected.SearchParamId, generation.Generation,
            affected.ResourceSurrogateId
        FROM @AffectedLastNResources AS affected
        INNER JOIN dbo.LastNCodeGroupGeneration AS generation WITH (UPDLOCK, HOLDLOCK)
            ON generation.ResourceTypeId = affected.ResourceTypeId
            AND generation.SearchParamId = affected.SearchParamId
            AND generation.State = 'Building'
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.LastNCodeGroupDirtyObservation AS dirty
            WHERE dirty.ResourceTypeId = affected.ResourceTypeId
                AND dirty.SearchParamId = affected.SearchParamId
                AND dirty.Generation = generation.Generation
                AND dirty.ResourceSurrogateId = affected.ResourceSurrogateId);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
