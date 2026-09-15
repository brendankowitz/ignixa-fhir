CREATE PROCEDURE dbo.CompleteLastNCodeGroupGeneration
    @ResourceTypeId SMALLINT,
    @SearchParamId SMALLINT,
    @Generation BIGINT,
    @AttemptId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @LockResult INT;
    DECLARE @LockResource NVARCHAR(255) =
        CONCAT('LastNCodeGroup:', @ResourceTypeId, ':', @SearchParamId);
    DECLARE @DirtyResources dbo.LastNResourceScopeList;
    DECLARE @FullScopeResources dbo.LastNResourceScopeList;
    DECLARE @CurrentResources dbo.LastNResourceScopeList;

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
                AND SearchParamId = @SearchParamId
                AND Generation = @Generation
                AND AttemptId = @AttemptId
                AND State = 'Building')
        BEGIN
            THROW 50424, 'LastN code-group generation is not active.', 1;
        END;

        WHILE 1 = 1
        BEGIN
            DELETE FROM @DirtyResources;

            INSERT INTO @DirtyResources (ResourceTypeId, SearchParamId, ResourceSurrogateId)
            SELECT dirty.ResourceTypeId, dirty.SearchParamId, dirty.ResourceSurrogateId
            FROM dbo.LastNCodeGroupDirtyObservation AS dirty WITH (UPDLOCK, HOLDLOCK)
            WHERE dirty.ResourceTypeId = @ResourceTypeId
                AND dirty.SearchParamId = @SearchParamId
                AND dirty.Generation = @Generation;

            IF @@ROWCOUNT = 0
                BREAK;

            DELETE dirty
            FROM dbo.LastNCodeGroupDirtyObservation AS dirty
            INNER JOIN @DirtyResources AS replay
                ON replay.ResourceTypeId = dirty.ResourceTypeId
                AND replay.SearchParamId = dirty.SearchParamId
                AND replay.ResourceSurrogateId = dirty.ResourceSurrogateId
            WHERE dirty.Generation = @Generation;

            EXEC dbo.MaintainLastNCodeGroups
                @Mode = 'Remove',
                @Resources = @DirtyResources;
            EXEC dbo.MaintainLastNCodeGroups
                @Mode = 'Add',
                @Resources = @DirtyResources;
        END;

        INSERT INTO @FullScopeResources (ResourceTypeId, SearchParamId, ResourceSurrogateId)
        SELECT @ResourceTypeId, @SearchParamId, membership.ResourceSurrogateId
        FROM dbo.LastNObservationCodeMembership AS membership
        WHERE membership.ResourceTypeId = @ResourceTypeId
            AND membership.SearchParamId = @SearchParamId
        UNION
        SELECT @ResourceTypeId, @SearchParamId, groupRow.ResourceSurrogateId
        FROM dbo.LastNObservationCodeGroup AS groupRow
        WHERE groupRow.ResourceTypeId = @ResourceTypeId
            AND groupRow.SearchParamId = @SearchParamId
        UNION
        SELECT @ResourceTypeId, @SearchParamId, resource.ResourceSurrogateId
        FROM dbo.Resource AS resource
        WHERE resource.ResourceTypeId = @ResourceTypeId
            AND resource.IsHistory = 0
            AND resource.IsDeleted = 0;

        INSERT INTO @CurrentResources (ResourceTypeId, SearchParamId, ResourceSurrogateId)
        SELECT @ResourceTypeId, @SearchParamId, resource.ResourceSurrogateId
        FROM dbo.Resource AS resource
        WHERE resource.ResourceTypeId = @ResourceTypeId
            AND resource.IsHistory = 0
            AND resource.IsDeleted = 0;

        EXEC dbo.MaintainLastNCodeGroups
            @Mode = 'Remove',
            @Resources = @FullScopeResources;
        EXEC dbo.MaintainLastNCodeGroups
            @Mode = 'Add',
            @Resources = @CurrentResources;

        IF EXISTS (
            SELECT 1
            FROM dbo.LastNObservationCodeMembership AS membership
            LEFT JOIN dbo.LastNCodeIdentity AS identityRow
                ON identityRow.CodeIdentityId = membership.CodeIdentityId
                AND identityRow.ResourceTypeId = membership.ResourceTypeId
                AND identityRow.SearchParamId = membership.SearchParamId
            WHERE membership.ResourceTypeId = @ResourceTypeId
                AND membership.SearchParamId = @SearchParamId
                AND identityRow.CodeIdentityId IS NULL)
        BEGIN
            THROW 50420, 'LastN membership invariant failed.', 1;
        END;

        IF EXISTS (
            SELECT membership.ResourceSurrogateId
            FROM dbo.LastNObservationCodeMembership AS membership
            WHERE membership.ResourceTypeId = @ResourceTypeId
                AND membership.SearchParamId = @SearchParamId
            GROUP BY membership.ResourceSurrogateId
            HAVING COUNT(DISTINCT membership.CodeIdentityId) > 0
                AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.LastNObservationCodeGroup AS groupRow
                    WHERE groupRow.ResourceTypeId = @ResourceTypeId
                        AND groupRow.SearchParamId = @SearchParamId
                        AND groupRow.ResourceSurrogateId = membership.ResourceSurrogateId))
        BEGIN
            THROW 50421, 'LastN coded group invariant failed.', 1;
        END;

        UPDATE dbo.LastNCodeGroupGeneration
        SET State = 'Ready',
            LeaseExpiresDateTime = NULL,
            CompletedDateTime = SYSUTCDATETIME(),
            FailureReason = NULL
        WHERE ResourceTypeId = @ResourceTypeId
            AND SearchParamId = @SearchParamId
            AND Generation = @Generation
            AND AttemptId = @AttemptId
            AND State = 'Building';

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
