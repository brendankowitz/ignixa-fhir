CREATE PROCEDURE dbo.UpdateResourceSearchParamsAndMaintainLastNGroups
    @FailedResources INT = 0 OUTPUT,
    @Resources dbo.ResourceList READONLY,
    @ResourceWriteClaims dbo.ResourceWriteClaimList READONLY,
    @ReferenceSearchParams dbo.ReferenceSearchParamList READONLY,
    @TokenSearchParams dbo.TokenSearchParamList READONLY,
    @TokenTexts dbo.TokenTextList READONLY,
    @StringSearchParams dbo.StringSearchParamList READONLY,
    @UriSearchParams dbo.UriSearchParamList READONLY,
    @NumberSearchParams dbo.NumberSearchParamList READONLY,
    @QuantitySearchParams dbo.QuantitySearchParamList READONLY,
    @DateTimeSearchParams dbo.DateTimeSearchParamList READONLY,
    @ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY,
    @TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY,
    @TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY,
    @TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY,
    @TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY,
    @TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @AffectedLastNResources dbo.LastNResourceScopeList;
    DECLARE @ResourceTypeId SMALLINT;
    DECLARE @SearchParamId SMALLINT;
    DECLARE @LockResult INT;
    DECLARE @LockResource NVARCHAR(255);

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO @AffectedLastNResources (ResourceTypeId, SearchParamId, ResourceSurrogateId)
        SELECT incoming.ResourceTypeId, generation.SearchParamId, incoming.ResourceSurrogateId
        FROM @Resources AS incoming
        INNER JOIN dbo.LastNCodeGroupGeneration AS generation
            ON generation.ResourceTypeId = incoming.ResourceTypeId;

        DECLARE scope_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT DISTINCT ResourceTypeId, SearchParamId
        FROM @AffectedLastNResources
        ORDER BY ResourceTypeId, SearchParamId;

        OPEN scope_cursor;
        FETCH NEXT FROM scope_cursor INTO @ResourceTypeId, @SearchParamId;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @LockResource = CONCAT('LastNCodeGroup:', @ResourceTypeId, ':', @SearchParamId);
            EXEC @LockResult = sys.sp_getapplock
                @Resource = @LockResource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 15000;
            IF @LockResult < 0
                THROW 50410, 'Unable to acquire LastN code-group scope lock.', 1;

            FETCH NEXT FROM scope_cursor INTO @ResourceTypeId, @SearchParamId;
        END;
        CLOSE scope_cursor;
        DEALLOCATE scope_cursor;

        EXEC dbo.MaintainLastNCodeGroups
            @Mode = 'Remove',
            @Resources = @AffectedLastNResources;

        EXEC dbo.UpdateResourceSearchParams
            @FailedResources = @FailedResources OUTPUT,
            @Resources = @Resources,
            @ResourceWriteClaims = @ResourceWriteClaims,
            @ReferenceSearchParams = @ReferenceSearchParams,
            @TokenSearchParams = @TokenSearchParams,
            @TokenTexts = @TokenTexts,
            @StringSearchParams = @StringSearchParams,
            @UriSearchParams = @UriSearchParams,
            @NumberSearchParams = @NumberSearchParams,
            @QuantitySearchParams = @QuantitySearchParams,
            @DateTimeSearchParams = @DateTimeSearchParams,
            @ReferenceTokenCompositeSearchParams = @ReferenceTokenCompositeSearchParams,
            @TokenTokenCompositeSearchParams = @TokenTokenCompositeSearchParams,
            @TokenDateTimeCompositeSearchParams = @TokenDateTimeCompositeSearchParams,
            @TokenQuantityCompositeSearchParams = @TokenQuantityCompositeSearchParams,
            @TokenStringCompositeSearchParams = @TokenStringCompositeSearchParams,
            @TokenNumberNumberCompositeSearchParams = @TokenNumberNumberCompositeSearchParams;

        EXEC dbo.MaintainLastNCodeGroups
            @Mode = 'Add',
            @Resources = @AffectedLastNResources;

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
