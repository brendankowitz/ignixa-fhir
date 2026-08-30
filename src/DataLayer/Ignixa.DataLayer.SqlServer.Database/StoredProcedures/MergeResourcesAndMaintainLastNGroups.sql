CREATE PROCEDURE dbo.MergeResourcesAndMaintainLastNGroups
    @AffectedRows INT = 0 OUTPUT,
    @RaiseExceptionOnConflict BIT = 1,
    @IsResourceChangeCaptureEnabled BIT = 0,
    @TransactionId BIGINT = NULL,
    @SingleTransaction BIT = 1,
    @Resources dbo.ResourceList READONLY,
    @ResourceWriteClaims dbo.ResourceWriteClaimList READONLY,
    @ReferenceSearchParams dbo.ReferenceSearchParamList READONLY,
    @TokenSearchParams dbo.TokenSearchParamList READONLY,
    @TokenTexts dbo.TokenTextList READONLY,
    @StringSearchParams dbo.StringSearchParamList READONLY,
    @UriSearchParams dbo.UriSearchParamList READONLY,
    @NumberSearchParams dbo.NumberSearchParamList READONLY,
    @QuantitySearchParams dbo.QuantitySearchParamList READONLY,
    @DateTimeSearchParms dbo.DateTimeSearchParamList READONLY,
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
    DECLARE @IsRetry BIT = 0;

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

        INSERT INTO @AffectedLastNResources (ResourceTypeId, SearchParamId, ResourceSurrogateId)
        SELECT incoming.ResourceTypeId, generation.SearchParamId, currentResource.ResourceSurrogateId
        FROM @Resources AS incoming
        INNER JOIN dbo.LastNCodeGroupGeneration AS generation
            ON generation.ResourceTypeId = incoming.ResourceTypeId
        INNER JOIN dbo.Resource AS currentResource
            ON currentResource.ResourceTypeId = incoming.ResourceTypeId
            AND currentResource.ResourceId = incoming.ResourceId
            AND currentResource.IsHistory = 0
        WHERE NOT EXISTS (
            SELECT 1
            FROM @AffectedLastNResources AS affected
            WHERE affected.ResourceTypeId = incoming.ResourceTypeId
                AND affected.SearchParamId = generation.SearchParamId
                AND affected.ResourceSurrogateId = currentResource.ResourceSurrogateId);

        IF EXISTS (SELECT 1 FROM @Resources)
            AND NOT EXISTS (
                SELECT 1
                FROM @Resources AS incoming
                LEFT JOIN dbo.Resource AS currentResource
                    ON currentResource.ResourceTypeId = incoming.ResourceTypeId
                    AND currentResource.ResourceSurrogateId = incoming.ResourceSurrogateId
                    AND currentResource.ResourceId = incoming.ResourceId
                    AND currentResource.Version = incoming.Version
                    AND currentResource.IsHistory = 0
                WHERE currentResource.ResourceSurrogateId IS NULL)
            SET @IsRetry = 1;

        EXEC dbo.MaintainLastNCodeGroups
            @Mode = 'Remove',
            @Resources = @AffectedLastNResources;

        IF @IsRetry = 0
        BEGIN
            EXEC dbo.MergeResources
                @AffectedRows = @AffectedRows OUTPUT,
                @RaiseExceptionOnConflict = @RaiseExceptionOnConflict,
                @IsResourceChangeCaptureEnabled = @IsResourceChangeCaptureEnabled,
                @TransactionId = @TransactionId,
                @SingleTransaction = @SingleTransaction,
                @Resources = @Resources,
                @ResourceWriteClaims = @ResourceWriteClaims,
                @ReferenceSearchParams = @ReferenceSearchParams,
                @TokenSearchParams = @TokenSearchParams,
                @TokenTexts = @TokenTexts,
                @StringSearchParams = @StringSearchParams,
                @UriSearchParams = @UriSearchParams,
                @NumberSearchParams = @NumberSearchParams,
                @QuantitySearchParams = @QuantitySearchParams,
                @DateTimeSearchParms = @DateTimeSearchParms,
                @ReferenceTokenCompositeSearchParams = @ReferenceTokenCompositeSearchParams,
                @TokenTokenCompositeSearchParams = @TokenTokenCompositeSearchParams,
                @TokenDateTimeCompositeSearchParams = @TokenDateTimeCompositeSearchParams,
                @TokenQuantityCompositeSearchParams = @TokenQuantityCompositeSearchParams,
                @TokenStringCompositeSearchParams = @TokenStringCompositeSearchParams,
                @TokenNumberNumberCompositeSearchParams = @TokenNumberNumberCompositeSearchParams;
        END;
        ELSE
            SET @AffectedRows = 0;

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
