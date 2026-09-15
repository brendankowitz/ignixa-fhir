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
    DECLARE @Start DATETIME = GETUTCDATE();
    DECLARE @RetryMode VARCHAR(200);
    DECLARE @Existing TABLE (
        ResourceTypeId SMALLINT NOT NULL,
        SurrogateId BIGINT NOT NULL,
        PRIMARY KEY (ResourceTypeId, SurrogateId)
    );

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

        INSERT INTO @Existing (ResourceTypeId, SurrogateId)
        SELECT currentResource.ResourceTypeId, currentResource.ResourceSurrogateId
        FROM @Resources AS incoming
        INNER JOIN dbo.Resource AS currentResource
            ON currentResource.ResourceTypeId = incoming.ResourceTypeId
            AND currentResource.ResourceSurrogateId = incoming.ResourceSurrogateId
            AND currentResource.ResourceId = incoming.ResourceId
            AND currentResource.Version = incoming.Version
            AND currentResource.IsHistory = 0;

        IF EXISTS (SELECT 1 FROM @Resources)
            AND (SELECT COUNT(*) FROM @Existing) = (SELECT COUNT(*) FROM @Resources)
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
        BEGIN
            SET @AffectedRows = 0;

            INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
            SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
            FROM @ResourceWriteClaims AS supplied
            WHERE EXISTS (
                    SELECT 1
                    FROM @Existing AS existing
                    WHERE existing.SurrogateId = supplied.ResourceSurrogateId)
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.ResourceWriteClaim AS currentRow
                    WHERE currentRow.ResourceSurrogateId = supplied.ResourceSurrogateId);
            SET @AffectedRows += @@ROWCOUNT;

            INSERT INTO dbo.ReferenceSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri,
                 ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion)
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri,
                ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion
            FROM @ReferenceSearchParams AS supplied
            WHERE EXISTS (
                    SELECT 1
                    FROM @Existing AS existing
                    WHERE existing.ResourceTypeId = supplied.ResourceTypeId
                        AND existing.SurrogateId = supplied.ResourceSurrogateId)
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.ReferenceSearchParam AS currentRow
                    WHERE currentRow.ResourceTypeId = supplied.ResourceTypeId
                        AND currentRow.ResourceSurrogateId = supplied.ResourceSurrogateId);
            SET @AffectedRows += @@ROWCOUNT;

            INSERT INTO dbo.TokenSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow)
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
            FROM @TokenSearchParams AS supplied
            WHERE EXISTS (
                    SELECT 1
                    FROM @Existing AS existing
                    WHERE existing.ResourceTypeId = supplied.ResourceTypeId
                        AND existing.SurrogateId = supplied.ResourceSurrogateId)
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.TokenSearchParam AS currentRow
                    WHERE currentRow.ResourceTypeId = supplied.ResourceTypeId
                        AND currentRow.ResourceSurrogateId = supplied.ResourceSurrogateId);
            SET @AffectedRows += @@ROWCOUNT;

            INSERT INTO dbo.TokenText
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text)
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text
            FROM @TokenTexts AS supplied
            WHERE EXISTS (
                    SELECT 1
                    FROM @Existing AS existing
                    WHERE existing.ResourceTypeId = supplied.ResourceTypeId
                        AND existing.SurrogateId = supplied.ResourceSurrogateId)
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.TokenText AS currentRow
                    WHERE currentRow.ResourceTypeId = supplied.ResourceTypeId
                        AND currentRow.ResourceSurrogateId = supplied.ResourceSurrogateId);
            SET @AffectedRows += @@ROWCOUNT;

            INSERT INTO dbo.StringSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax)
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax
            FROM @StringSearchParams AS supplied
            WHERE EXISTS (
                    SELECT 1
                    FROM @Existing AS existing
                    WHERE existing.ResourceTypeId = supplied.ResourceTypeId
                        AND existing.SurrogateId = supplied.ResourceSurrogateId)
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.StringSearchParam AS currentRow
                    WHERE currentRow.ResourceTypeId = supplied.ResourceTypeId
                        AND currentRow.ResourceSurrogateId = supplied.ResourceSurrogateId);
            SET @AffectedRows += @@ROWCOUNT;

            INSERT INTO dbo.UriSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri
            FROM @UriSearchParams AS supplied
            WHERE EXISTS (
                    SELECT 1
                    FROM @Existing AS existing
                    WHERE existing.ResourceTypeId = supplied.ResourceTypeId
                        AND existing.SurrogateId = supplied.ResourceSurrogateId)
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.UriSearchParam AS currentRow
                    WHERE currentRow.ResourceTypeId = supplied.ResourceTypeId
                        AND currentRow.ResourceSurrogateId = supplied.ResourceSurrogateId);
            SET @AffectedRows += @@ROWCOUNT;

            INSERT INTO dbo.NumberSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue)
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
            FROM @NumberSearchParams AS supplied
            WHERE EXISTS (
                    SELECT 1
                    FROM @Existing AS existing
                    WHERE existing.ResourceTypeId = supplied.ResourceTypeId
                        AND existing.SurrogateId = supplied.ResourceSurrogateId)
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.NumberSearchParam AS currentRow
                    WHERE currentRow.ResourceTypeId = supplied.ResourceTypeId
                        AND currentRow.ResourceSurrogateId = supplied.ResourceSurrogateId);
            SET @AffectedRows += @@ROWCOUNT;

            INSERT INTO dbo.QuantitySearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId,
                 SingleValue, LowValue, HighValue)
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId,
                SingleValue, LowValue, HighValue
            FROM @QuantitySearchParams AS supplied
            WHERE EXISTS (
                    SELECT 1
                    FROM @Existing AS existing
                    WHERE existing.ResourceTypeId = supplied.ResourceTypeId
                        AND existing.SurrogateId = supplied.ResourceSurrogateId)
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.QuantitySearchParam AS currentRow
                    WHERE currentRow.ResourceTypeId = supplied.ResourceTypeId
                        AND currentRow.ResourceSurrogateId = supplied.ResourceSurrogateId);
            SET @AffectedRows += @@ROWCOUNT;

            INSERT INTO dbo.DateTimeSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime,
                 IsLongerThanADay, IsMin, IsMax)
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime,
                IsLongerThanADay, IsMin, IsMax
            FROM @DateTimeSearchParms AS supplied
            WHERE EXISTS (
                    SELECT 1
                    FROM @Existing AS existing
                    WHERE existing.ResourceTypeId = supplied.ResourceTypeId
                        AND existing.SurrogateId = supplied.ResourceSurrogateId)
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.DateTimeSearchParam AS currentRow
                    WHERE currentRow.ResourceTypeId = supplied.ResourceTypeId
                        AND currentRow.ResourceSurrogateId = supplied.ResourceSurrogateId);
            SET @AffectedRows += @@ROWCOUNT;

            INSERT INTO dbo.ReferenceTokenCompositeSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1,
                 ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1,
                 SystemId2, Code2, CodeOverflow2)
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1,
                ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1,
                SystemId2, Code2, CodeOverflow2
            FROM @ReferenceTokenCompositeSearchParams AS supplied
            WHERE EXISTS (
                    SELECT 1
                    FROM @Existing AS existing
                    WHERE existing.ResourceTypeId = supplied.ResourceTypeId
                        AND existing.SurrogateId = supplied.ResourceSurrogateId)
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.ReferenceTokenCompositeSearchParam AS currentRow
                    WHERE currentRow.ResourceTypeId = supplied.ResourceTypeId
                        AND currentRow.ResourceSurrogateId = supplied.ResourceSurrogateId);
            SET @AffectedRows += @@ROWCOUNT;

            INSERT INTO dbo.TokenTokenCompositeSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1,
                 CodeOverflow1, SystemId2, Code2, CodeOverflow2)
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1,
                CodeOverflow1, SystemId2, Code2, CodeOverflow2
            FROM @TokenTokenCompositeSearchParams AS supplied
            WHERE EXISTS (
                    SELECT 1
                    FROM @Existing AS existing
                    WHERE existing.ResourceTypeId = supplied.ResourceTypeId
                        AND existing.SurrogateId = supplied.ResourceSurrogateId)
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.TokenTokenCompositeSearchParam AS currentRow
                    WHERE currentRow.ResourceTypeId = supplied.ResourceTypeId
                        AND currentRow.ResourceSurrogateId = supplied.ResourceSurrogateId);
            SET @AffectedRows += @@ROWCOUNT;

            INSERT INTO dbo.TokenDateTimeCompositeSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1,
                 CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2)
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1,
                CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2
            FROM @TokenDateTimeCompositeSearchParams AS supplied
            WHERE EXISTS (
                    SELECT 1
                    FROM @Existing AS existing
                    WHERE existing.ResourceTypeId = supplied.ResourceTypeId
                        AND existing.SurrogateId = supplied.ResourceSurrogateId)
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.TokenDateTimeCompositeSearchParam AS currentRow
                    WHERE currentRow.ResourceTypeId = supplied.ResourceTypeId
                        AND currentRow.ResourceSurrogateId = supplied.ResourceSurrogateId);
            SET @AffectedRows += @@ROWCOUNT;

            INSERT INTO dbo.TokenQuantityCompositeSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1,
                 CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2)
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1,
                CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2
            FROM @TokenQuantityCompositeSearchParams AS supplied
            WHERE EXISTS (
                    SELECT 1
                    FROM @Existing AS existing
                    WHERE existing.ResourceTypeId = supplied.ResourceTypeId
                        AND existing.SurrogateId = supplied.ResourceSurrogateId)
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.TokenQuantityCompositeSearchParam AS currentRow
                    WHERE currentRow.ResourceTypeId = supplied.ResourceTypeId
                        AND currentRow.ResourceSurrogateId = supplied.ResourceSurrogateId);
            SET @AffectedRows += @@ROWCOUNT;

            INSERT INTO dbo.TokenStringCompositeSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1,
                 CodeOverflow1, Text2, TextOverflow2)
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1,
                CodeOverflow1, Text2, TextOverflow2
            FROM @TokenStringCompositeSearchParams AS supplied
            WHERE EXISTS (
                    SELECT 1
                    FROM @Existing AS existing
                    WHERE existing.ResourceTypeId = supplied.ResourceTypeId
                        AND existing.SurrogateId = supplied.ResourceSurrogateId)
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.TokenStringCompositeSearchParam AS currentRow
                    WHERE currentRow.ResourceTypeId = supplied.ResourceTypeId
                        AND currentRow.ResourceSurrogateId = supplied.ResourceSurrogateId);
            SET @AffectedRows += @@ROWCOUNT;

            INSERT INTO dbo.TokenNumberNumberCompositeSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1,
                 CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3,
                 LowValue3, HighValue3, HasRange)
            SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1,
                CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3,
                LowValue3, HighValue3, HasRange
            FROM @TokenNumberNumberCompositeSearchParams AS supplied
            WHERE EXISTS (
                    SELECT 1
                    FROM @Existing AS existing
                    WHERE existing.ResourceTypeId = supplied.ResourceTypeId
                        AND existing.SurrogateId = supplied.ResourceSurrogateId)
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.TokenNumberNumberCompositeSearchParam AS currentRow
                    WHERE currentRow.ResourceTypeId = supplied.ResourceTypeId
                        AND currentRow.ResourceSurrogateId = supplied.ResourceSurrogateId);
            SET @AffectedRows += @@ROWCOUNT;

            IF @IsResourceChangeCaptureEnabled = 1
                EXEC dbo.CaptureResourceIdsForChanges @Resources;

            IF @TransactionId IS NOT NULL
                EXEC dbo.MergeResourcesCommitTransaction @TransactionId;

            SET @RetryMode = ISNULL((
                SELECT 'RT=[' + CONVERT(VARCHAR, MIN(ResourceTypeId))
                    + ',' + CONVERT(VARCHAR, MAX(ResourceTypeId))
                    + '] Sur=[' + CONVERT(VARCHAR, MIN(ResourceSurrogateId))
                    + ',' + CONVERT(VARCHAR, MAX(ResourceSurrogateId))
                    + '] V=' + CONVERT(VARCHAR, MAX(Version))
                    + ' Rows=' + CONVERT(VARCHAR, COUNT(*))
                FROM @Resources), 'Input=Empty');
            SET @RetryMode += ' E=' + CONVERT(VARCHAR, @RaiseExceptionOnConflict)
                + ' CC=' + CONVERT(VARCHAR, @IsResourceChangeCaptureEnabled)
                + ' IT=0 T=' + ISNULL(CONVERT(VARCHAR, @TransactionId), 'NULL')
                + ' ST=' + CONVERT(VARCHAR, @SingleTransaction)
                + ' R=1';
            EXEC dbo.LogEvent
                @Process = 'MergeResources',
                @Mode = @RetryMode,
                @Status = 'End',
                @Start = @Start,
                @Rows = @AffectedRows;
        END;

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
