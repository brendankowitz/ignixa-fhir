CREATE PROCEDURE dbo.MaintainLastNCodeGroups
    @Mode VARCHAR(8),
    @Resources dbo.LastNResourceScopeList READONLY
AS
BEGIN
    SET NOCOUNT ON;

    IF @Mode NOT IN ('Remove', 'Add')
        THROW 50400, 'MaintainLastNCodeGroups mode must be Remove or Add.', 1;

    DECLARE @InitialTransactionCount INT = @@TRANCOUNT;

    IF @InitialTransactionCount = 0
        BEGIN TRANSACTION;
    ELSE
        SAVE TRANSACTION MaintainLastNCodeGroups;

    BEGIN TRY
        SELECT ResourceTypeId, SearchParamId, ResourceSurrogateId
        INTO #resources
        FROM @Resources;

        CREATE UNIQUE CLUSTERED INDEX IX_resources
            ON #resources (ResourceTypeId, SearchParamId, ResourceSurrogateId);

        CREATE TABLE #oldMembership (
            ResourceTypeId SMALLINT NOT NULL,
            SearchParamId SMALLINT NOT NULL,
            ResourceSurrogateId BIGINT NOT NULL,
            CodeIdentityId BIGINT NOT NULL,
            ComponentCodeIdentityId BIGINT NOT NULL,
            PRIMARY KEY (ResourceTypeId, SearchParamId, ResourceSurrogateId, CodeIdentityId)
        );

        CREATE TABLE #removedPairs (
            ResourceTypeId SMALLINT NOT NULL,
            SearchParamId SMALLINT NOT NULL,
            ResourceSurrogateId BIGINT NOT NULL,
            LeftCodeIdentityId BIGINT NOT NULL,
            RightCodeIdentityId BIGINT NOT NULL,
            PRIMARY KEY (
                ResourceTypeId,
                SearchParamId,
                ResourceSurrogateId,
                LeftCodeIdentityId,
                RightCodeIdentityId)
        );

        CREATE TABLE #desiredMembership (
            ResourceTypeId SMALLINT NOT NULL,
            SearchParamId SMALLINT NOT NULL,
            ResourceSurrogateId BIGINT NOT NULL,
            CodeIdentityId BIGINT NOT NULL,
            PRIMARY KEY (ResourceTypeId, SearchParamId, ResourceSurrogateId, CodeIdentityId)
        );

        IF @Mode = 'Remove'
        BEGIN
            INSERT INTO #oldMembership
                (ResourceTypeId, SearchParamId, ResourceSurrogateId, CodeIdentityId, ComponentCodeIdentityId)
            SELECT DISTINCT
                membership.ResourceTypeId,
                membership.SearchParamId,
                membership.ResourceSurrogateId,
                membership.CodeIdentityId,
                identityRow.ComponentCodeIdentityId
            FROM #resources AS resourceScope
            INNER JOIN dbo.LastNObservationCodeMembership AS membership WITH (UPDLOCK, HOLDLOCK)
                ON membership.ResourceTypeId = resourceScope.ResourceTypeId
                AND membership.SearchParamId = resourceScope.SearchParamId
                AND membership.ResourceSurrogateId = resourceScope.ResourceSurrogateId
            INNER JOIN dbo.LastNCodeIdentity AS identityRow
                ON identityRow.CodeIdentityId = membership.CodeIdentityId
                AND identityRow.ResourceTypeId = membership.ResourceTypeId
                AND identityRow.SearchParamId = membership.SearchParamId;

            INSERT INTO #removedPairs
                (ResourceTypeId, SearchParamId, ResourceSurrogateId, LeftCodeIdentityId, RightCodeIdentityId)
            SELECT DISTINCT
                leftMember.ResourceTypeId,
                leftMember.SearchParamId,
                leftMember.ResourceSurrogateId,
                leftMember.CodeIdentityId,
                rightMember.CodeIdentityId
            FROM #oldMembership AS leftMember
            INNER JOIN #oldMembership AS rightMember
                ON rightMember.ResourceTypeId = leftMember.ResourceTypeId
                AND rightMember.SearchParamId = leftMember.SearchParamId
                AND rightMember.ResourceSurrogateId = leftMember.ResourceSurrogateId
                AND leftMember.CodeIdentityId < rightMember.CodeIdentityId;

            CREATE TABLE #removedEdgeCount (
                ResourceTypeId SMALLINT NOT NULL,
                SearchParamId SMALLINT NOT NULL,
                LeftCodeIdentityId BIGINT NOT NULL,
                RightCodeIdentityId BIGINT NOT NULL,
                SupportToRemove INT NOT NULL,
                PRIMARY KEY (ResourceTypeId, SearchParamId, LeftCodeIdentityId, RightCodeIdentityId)
            );

            INSERT INTO #removedEdgeCount
                (ResourceTypeId, SearchParamId, LeftCodeIdentityId, RightCodeIdentityId, SupportToRemove)
            SELECT
                ResourceTypeId,
                SearchParamId,
                LeftCodeIdentityId,
                RightCodeIdentityId,
                COUNT(*)
            FROM #removedPairs
            GROUP BY ResourceTypeId, SearchParamId, LeftCodeIdentityId, RightCodeIdentityId;

            IF EXISTS (
                SELECT 1
                FROM #removedEdgeCount AS removedEdge
                LEFT JOIN dbo.LastNCodeEdge AS edge WITH (UPDLOCK, HOLDLOCK)
                    ON edge.ResourceTypeId = removedEdge.ResourceTypeId
                    AND edge.SearchParamId = removedEdge.SearchParamId
                    AND edge.LeftCodeIdentityId = removedEdge.LeftCodeIdentityId
                    AND edge.RightCodeIdentityId = removedEdge.RightCodeIdentityId
                WHERE edge.LeftCodeIdentityId IS NULL OR edge.SupportCount < removedEdge.SupportToRemove)
            BEGIN
                THROW 50401, 'MaintainLastNCodeGroups found a missing or invalid edge support count.', 1;
            END;

            DELETE edge
            FROM dbo.LastNCodeEdge AS edge
            INNER JOIN #removedEdgeCount AS removedEdge
                ON removedEdge.ResourceTypeId = edge.ResourceTypeId
                AND removedEdge.SearchParamId = edge.SearchParamId
                AND removedEdge.LeftCodeIdentityId = edge.LeftCodeIdentityId
                AND removedEdge.RightCodeIdentityId = edge.RightCodeIdentityId
            WHERE edge.SupportCount = removedEdge.SupportToRemove;

            UPDATE edge
            SET SupportCount = edge.SupportCount - removedEdge.SupportToRemove
            FROM dbo.LastNCodeEdge AS edge
            INNER JOIN #removedEdgeCount AS removedEdge
                ON removedEdge.ResourceTypeId = edge.ResourceTypeId
                AND removedEdge.SearchParamId = edge.SearchParamId
                AND removedEdge.LeftCodeIdentityId = edge.LeftCodeIdentityId
                AND removedEdge.RightCodeIdentityId = edge.RightCodeIdentityId
            WHERE edge.SupportCount > removedEdge.SupportToRemove;

            DELETE groupRow
            FROM dbo.LastNObservationCodeGroup AS groupRow
            INNER JOIN #resources AS resourceScope
                ON resourceScope.ResourceTypeId = groupRow.ResourceTypeId
                AND resourceScope.SearchParamId = groupRow.SearchParamId
                AND resourceScope.ResourceSurrogateId = groupRow.ResourceSurrogateId;

            DELETE membership
            FROM dbo.LastNObservationCodeMembership AS membership
            INNER JOIN #resources AS resourceScope
                ON resourceScope.ResourceTypeId = membership.ResourceTypeId
                AND resourceScope.SearchParamId = membership.SearchParamId
                AND resourceScope.ResourceSurrogateId = membership.ResourceSurrogateId;
        END;

        IF @Mode = 'Add'
        BEGIN
            CREATE TABLE #sourceCoding (
                ResourceTypeId SMALLINT NOT NULL,
                SearchParamId SMALLINT NOT NULL,
                ResourceSurrogateId BIGINT NOT NULL,
                SystemId INT NULL,
                Code VARCHAR(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
                CodeOverflow VARCHAR(MAX) COLLATE Latin1_General_100_CS_AS NULL,
                CodeHash BINARY(32) NOT NULL
            );

            INSERT INTO #sourceCoding
                (ResourceTypeId, SearchParamId, ResourceSurrogateId, SystemId, Code, CodeOverflow, CodeHash)
            SELECT DISTINCT
                token.ResourceTypeId,
                token.SearchParamId,
                token.ResourceSurrogateId,
                token.SystemId,
                token.Code,
                token.CodeOverflow,
                HASHBYTES(
                    'SHA2_256',
                    CAST(CASE WHEN token.SystemId IS NULL THEN 0x00 ELSE 0x01 END AS VARBINARY(MAX))
                    + CASE
                        WHEN token.SystemId IS NULL THEN 0x
                        ELSE CONVERT(BINARY(4), token.SystemId)
                    END
                    + CONVERT(BINARY(4), DATALENGTH(token.Code))
                    + CONVERT(VARBINARY(MAX), token.Code)
                    + CAST(CASE WHEN token.CodeOverflow IS NULL THEN 0x00 ELSE 0x01 END AS BINARY(1))
                    + CASE
                        WHEN token.CodeOverflow IS NULL THEN 0x
                        ELSE CONVERT(BINARY(4), DATALENGTH(token.CodeOverflow))
                            + CONVERT(VARBINARY(MAX), token.CodeOverflow)
                    END)
            FROM #resources AS resourceScope
            INNER JOIN dbo.Resource AS resourceRow
                ON resourceRow.ResourceTypeId = resourceScope.ResourceTypeId
                AND resourceRow.ResourceSurrogateId = resourceScope.ResourceSurrogateId
                AND resourceRow.IsHistory = 0
                AND resourceRow.IsDeleted = 0
            INNER JOIN dbo.TokenSearchParam AS token
                ON token.ResourceTypeId = resourceScope.ResourceTypeId
                AND token.SearchParamId = resourceScope.SearchParamId
                AND token.ResourceSurrogateId = resourceScope.ResourceSurrogateId;

            CREATE TABLE #sourceText (
                ResourceTypeId SMALLINT NOT NULL,
                SearchParamId SMALLINT NOT NULL,
                ResourceSurrogateId BIGINT NOT NULL,
                TextCode NVARCHAR(400) COLLATE Latin1_General_100_CS_AS NOT NULL,
                PRIMARY KEY (ResourceTypeId, SearchParamId, ResourceSurrogateId, TextCode)
            );

            INSERT INTO #sourceText (ResourceTypeId, SearchParamId, ResourceSurrogateId, TextCode)
            SELECT DISTINCT
                textRow.ResourceTypeId,
                textRow.SearchParamId,
                textRow.ResourceSurrogateId,
                textRow.Text COLLATE Latin1_General_100_CS_AS
            FROM #resources AS resourceScope
            INNER JOIN dbo.Resource AS resourceRow
                ON resourceRow.ResourceTypeId = resourceScope.ResourceTypeId
                AND resourceRow.ResourceSurrogateId = resourceScope.ResourceSurrogateId
                AND resourceRow.IsHistory = 0
                AND resourceRow.IsDeleted = 0
            INNER JOIN dbo.TokenText AS textRow
                ON textRow.ResourceTypeId = resourceScope.ResourceTypeId
                AND textRow.SearchParamId = resourceScope.SearchParamId
                AND textRow.ResourceSurrogateId = resourceScope.ResourceSurrogateId
                AND textRow.IsHistory = 0
            WHERE NOT EXISTS (
                SELECT 1
                FROM #sourceCoding AS coding
                WHERE coding.ResourceTypeId = resourceScope.ResourceTypeId
                    AND coding.SearchParamId = resourceScope.SearchParamId
                    AND coding.ResourceSurrogateId = resourceScope.ResourceSurrogateId);

            IF EXISTS (
                SELECT 1
                FROM #sourceText
                GROUP BY ResourceTypeId, SearchParamId, ResourceSurrogateId
                HAVING COUNT(*) > 1)
            BEGIN
                THROW 50402, 'MaintainLastNCodeGroups found multiple text-only values for one Observation.', 1;
            END;

            CREATE TABLE #existingMembership (
                ResourceTypeId SMALLINT NOT NULL,
                SearchParamId SMALLINT NOT NULL,
                ResourceSurrogateId BIGINT NOT NULL,
                CodeIdentityId BIGINT NOT NULL,
                PRIMARY KEY (ResourceTypeId, SearchParamId, ResourceSurrogateId, CodeIdentityId)
            );

            INSERT INTO #existingMembership
                (ResourceTypeId, SearchParamId, ResourceSurrogateId, CodeIdentityId)
            SELECT
                membership.ResourceTypeId,
                membership.SearchParamId,
                membership.ResourceSurrogateId,
                membership.CodeIdentityId
            FROM #resources AS resourceScope
            INNER JOIN dbo.LastNObservationCodeMembership AS membership WITH (UPDLOCK, HOLDLOCK)
                ON membership.ResourceTypeId = resourceScope.ResourceTypeId
                AND membership.SearchParamId = resourceScope.SearchParamId
                AND membership.ResourceSurrogateId = resourceScope.ResourceSurrogateId;

            CREATE TABLE #newIdentity (
                CodeIdentityId BIGINT NOT NULL PRIMARY KEY
            );

            BEGIN TRY
                INSERT INTO dbo.LastNCodeIdentity
                    (ResourceTypeId, SearchParamId, SystemId, Code, CodeOverflow, CodeHash,
                     ComponentCodeIdentityId)
                OUTPUT inserted.CodeIdentityId INTO #newIdentity (CodeIdentityId)
                SELECT DISTINCT
                    sourceCoding.ResourceTypeId,
                    sourceCoding.SearchParamId,
                    sourceCoding.SystemId,
                    sourceCoding.Code,
                    sourceCoding.CodeOverflow,
                    sourceCoding.CodeHash,
                    0
                FROM #sourceCoding AS sourceCoding
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM dbo.LastNCodeIdentity AS identityRow WITH (UPDLOCK, HOLDLOCK)
                    WHERE identityRow.ResourceTypeId = sourceCoding.ResourceTypeId
                        AND identityRow.SearchParamId = sourceCoding.SearchParamId
                        AND identityRow.CodeHash = sourceCoding.CodeHash
                        AND (identityRow.SystemId = sourceCoding.SystemId
                            OR (identityRow.SystemId IS NULL AND sourceCoding.SystemId IS NULL))
                        AND identityRow.Code = sourceCoding.Code
                        AND (identityRow.CodeOverflow = sourceCoding.CodeOverflow
                            OR (identityRow.CodeOverflow IS NULL AND sourceCoding.CodeOverflow IS NULL)));
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2601, 2627)
                    THROW;
            END CATCH;

            UPDATE identityRow
            SET ComponentCodeIdentityId = identityRow.CodeIdentityId
            FROM dbo.LastNCodeIdentity AS identityRow
            INNER JOIN #newIdentity AS newIdentity
                ON newIdentity.CodeIdentityId = identityRow.CodeIdentityId;

            INSERT INTO #desiredMembership
                (ResourceTypeId, SearchParamId, ResourceSurrogateId, CodeIdentityId)
            SELECT DISTINCT
                sourceCoding.ResourceTypeId,
                sourceCoding.SearchParamId,
                sourceCoding.ResourceSurrogateId,
                matchingIdentity.CodeIdentityId
            FROM #sourceCoding AS sourceCoding
            CROSS APPLY (
                SELECT TOP (1) identityRow.CodeIdentityId
                FROM dbo.LastNCodeIdentity AS identityRow WITH (UPDLOCK, HOLDLOCK)
                WHERE identityRow.ResourceTypeId = sourceCoding.ResourceTypeId
                    AND identityRow.SearchParamId = sourceCoding.SearchParamId
                    AND identityRow.CodeHash = sourceCoding.CodeHash
                    AND (identityRow.SystemId = sourceCoding.SystemId
                        OR (identityRow.SystemId IS NULL AND sourceCoding.SystemId IS NULL))
                    AND identityRow.Code = sourceCoding.Code
                    AND (identityRow.CodeOverflow = sourceCoding.CodeOverflow
                        OR (identityRow.CodeOverflow IS NULL AND sourceCoding.CodeOverflow IS NULL))
                ORDER BY identityRow.CodeIdentityId
            ) AS matchingIdentity;

            IF (SELECT COUNT(*) FROM #desiredMembership) <> (
                SELECT COUNT(*)
                FROM (
                    SELECT DISTINCT ResourceTypeId, SearchParamId, ResourceSurrogateId, SystemId, Code, CodeOverflow
                    FROM #sourceCoding
                ) AS sourceIdentity)
            BEGIN
                THROW 50401, 'MaintainLastNCodeGroups could not resolve a code identity.', 1;
            END;

            CREATE TABLE #existingPairs (
                ResourceTypeId SMALLINT NOT NULL,
                SearchParamId SMALLINT NOT NULL,
                ResourceSurrogateId BIGINT NOT NULL,
                LeftCodeIdentityId BIGINT NOT NULL,
                RightCodeIdentityId BIGINT NOT NULL,
                PRIMARY KEY (
                    ResourceTypeId,
                    SearchParamId,
                    ResourceSurrogateId,
                    LeftCodeIdentityId,
                    RightCodeIdentityId)
            );

            INSERT INTO #existingPairs
                (ResourceTypeId, SearchParamId, ResourceSurrogateId, LeftCodeIdentityId, RightCodeIdentityId)
            SELECT
                leftMember.ResourceTypeId,
                leftMember.SearchParamId,
                leftMember.ResourceSurrogateId,
                leftMember.CodeIdentityId,
                rightMember.CodeIdentityId
            FROM #existingMembership AS leftMember
            INNER JOIN #existingMembership AS rightMember
                ON rightMember.ResourceTypeId = leftMember.ResourceTypeId
                AND rightMember.SearchParamId = leftMember.SearchParamId
                AND rightMember.ResourceSurrogateId = leftMember.ResourceSurrogateId
                AND leftMember.CodeIdentityId < rightMember.CodeIdentityId;

            CREATE TABLE #addedPairs (
                ResourceTypeId SMALLINT NOT NULL,
                SearchParamId SMALLINT NOT NULL,
                LeftCodeIdentityId BIGINT NOT NULL,
                RightCodeIdentityId BIGINT NOT NULL,
                SupportToAdd INT NOT NULL,
                PRIMARY KEY (ResourceTypeId, SearchParamId, LeftCodeIdentityId, RightCodeIdentityId)
            );

            INSERT INTO #addedPairs
                (ResourceTypeId, SearchParamId, LeftCodeIdentityId, RightCodeIdentityId, SupportToAdd)
            SELECT
                desiredPair.ResourceTypeId,
                desiredPair.SearchParamId,
                desiredPair.LeftCodeIdentityId,
                desiredPair.RightCodeIdentityId,
                COUNT(*)
            FROM (
                SELECT
                    leftMember.ResourceTypeId,
                    leftMember.SearchParamId,
                    leftMember.ResourceSurrogateId,
                    leftMember.CodeIdentityId AS LeftCodeIdentityId,
                    rightMember.CodeIdentityId AS RightCodeIdentityId
                FROM #desiredMembership AS leftMember
                INNER JOIN #desiredMembership AS rightMember
                    ON rightMember.ResourceTypeId = leftMember.ResourceTypeId
                    AND rightMember.SearchParamId = leftMember.SearchParamId
                    AND rightMember.ResourceSurrogateId = leftMember.ResourceSurrogateId
                    AND leftMember.CodeIdentityId < rightMember.CodeIdentityId
            ) AS desiredPair
            LEFT JOIN #existingPairs AS existingPair
                ON existingPair.ResourceTypeId = desiredPair.ResourceTypeId
                AND existingPair.SearchParamId = desiredPair.SearchParamId
                AND existingPair.ResourceSurrogateId = desiredPair.ResourceSurrogateId
                AND existingPair.LeftCodeIdentityId = desiredPair.LeftCodeIdentityId
                AND existingPair.RightCodeIdentityId = desiredPair.RightCodeIdentityId
            WHERE existingPair.LeftCodeIdentityId IS NULL
            GROUP BY
                desiredPair.ResourceTypeId,
                desiredPair.SearchParamId,
                desiredPair.LeftCodeIdentityId,
                desiredPair.RightCodeIdentityId;

            INSERT INTO dbo.LastNObservationCodeMembership
                (ResourceTypeId, SearchParamId, ResourceSurrogateId, CodeIdentityId)
            SELECT
                desiredMember.ResourceTypeId,
                desiredMember.SearchParamId,
                desiredMember.ResourceSurrogateId,
                desiredMember.CodeIdentityId
            FROM #desiredMembership AS desiredMember
            WHERE NOT EXISTS (
                SELECT 1
                FROM dbo.LastNObservationCodeMembership AS existingMember WITH (UPDLOCK, HOLDLOCK)
                WHERE existingMember.ResourceTypeId = desiredMember.ResourceTypeId
                    AND existingMember.SearchParamId = desiredMember.SearchParamId
                    AND existingMember.ResourceSurrogateId = desiredMember.ResourceSurrogateId
                    AND existingMember.CodeIdentityId = desiredMember.CodeIdentityId);

            UPDATE edge WITH (UPDLOCK, HOLDLOCK)
            SET SupportCount = edge.SupportCount + addedPair.SupportToAdd
            FROM dbo.LastNCodeEdge AS edge
            INNER JOIN #addedPairs AS addedPair
                ON addedPair.ResourceTypeId = edge.ResourceTypeId
                AND addedPair.SearchParamId = edge.SearchParamId
                AND addedPair.LeftCodeIdentityId = edge.LeftCodeIdentityId
                AND addedPair.RightCodeIdentityId = edge.RightCodeIdentityId;

            INSERT INTO dbo.LastNCodeEdge
                (ResourceTypeId, SearchParamId, LeftCodeIdentityId, RightCodeIdentityId, SupportCount)
            SELECT
                addedPair.ResourceTypeId,
                addedPair.SearchParamId,
                addedPair.LeftCodeIdentityId,
                addedPair.RightCodeIdentityId,
                addedPair.SupportToAdd
            FROM #addedPairs AS addedPair
            WHERE NOT EXISTS (
                SELECT 1
                FROM dbo.LastNCodeEdge AS edge WITH (UPDLOCK, HOLDLOCK)
                WHERE edge.ResourceTypeId = addedPair.ResourceTypeId
                    AND edge.SearchParamId = addedPair.SearchParamId
                    AND edge.LeftCodeIdentityId = addedPair.LeftCodeIdentityId
                    AND edge.RightCodeIdentityId = addedPair.RightCodeIdentityId);

            DELETE groupRow
            FROM dbo.LastNObservationCodeGroup AS groupRow
            INNER JOIN #resources AS resourceScope
                ON resourceScope.ResourceTypeId = groupRow.ResourceTypeId
                AND resourceScope.SearchParamId = groupRow.SearchParamId
                AND resourceScope.ResourceSurrogateId = groupRow.ResourceSurrogateId;

            INSERT INTO dbo.LastNObservationCodeGroup
                (ResourceTypeId, SearchParamId, ResourceSurrogateId, GroupKind, CodeGroupId, TextCode)
            SELECT
                desiredMember.ResourceTypeId,
                desiredMember.SearchParamId,
                desiredMember.ResourceSurrogateId,
                0,
                MIN(identityRow.ComponentCodeIdentityId),
                NULL
            FROM #desiredMembership AS desiredMember
            INNER JOIN dbo.LastNCodeIdentity AS identityRow
                ON identityRow.CodeIdentityId = desiredMember.CodeIdentityId
            GROUP BY
                desiredMember.ResourceTypeId,
                desiredMember.SearchParamId,
                desiredMember.ResourceSurrogateId;

            INSERT INTO dbo.LastNObservationCodeGroup
                (ResourceTypeId, SearchParamId, ResourceSurrogateId, GroupKind, CodeGroupId, TextCode)
            SELECT
                sourceText.ResourceTypeId,
                sourceText.SearchParamId,
                sourceText.ResourceSurrogateId,
                1,
                NULL,
                sourceText.TextCode
            FROM #sourceText AS sourceText;
        END;

        CREATE TABLE #affectedIdentity (
            ResourceTypeId SMALLINT NOT NULL,
            SearchParamId SMALLINT NOT NULL,
            CodeIdentityId BIGINT NOT NULL,
            PRIMARY KEY (ResourceTypeId, SearchParamId, CodeIdentityId)
        );

        INSERT INTO #affectedIdentity (ResourceTypeId, SearchParamId, CodeIdentityId)
        SELECT ResourceTypeId, SearchParamId, CodeIdentityId
        FROM #oldMembership
        UNION
        SELECT ResourceTypeId, SearchParamId, LeftCodeIdentityId
        FROM #removedPairs
        UNION
        SELECT ResourceTypeId, SearchParamId, RightCodeIdentityId
        FROM #removedPairs
        UNION
        SELECT ResourceTypeId, SearchParamId, CodeIdentityId
        FROM #desiredMembership;

        CREATE TABLE #affectedComponent (
            ResourceTypeId SMALLINT NOT NULL,
            SearchParamId SMALLINT NOT NULL,
            ComponentCodeIdentityId BIGINT NOT NULL,
            PRIMARY KEY (ResourceTypeId, SearchParamId, ComponentCodeIdentityId)
        );

        INSERT INTO #affectedComponent (ResourceTypeId, SearchParamId, ComponentCodeIdentityId)
        SELECT ResourceTypeId, SearchParamId, ComponentCodeIdentityId
        FROM #oldMembership
        UNION
        SELECT
            affectedIdentity.ResourceTypeId,
            affectedIdentity.SearchParamId,
            identityRow.ComponentCodeIdentityId
        FROM #affectedIdentity AS affectedIdentity
        INNER JOIN dbo.LastNCodeIdentity AS identityRow
            ON identityRow.CodeIdentityId = affectedIdentity.CodeIdentityId
            AND identityRow.ResourceTypeId = affectedIdentity.ResourceTypeId
            AND identityRow.SearchParamId = affectedIdentity.SearchParamId;

        CREATE TABLE #labels (
            ResourceTypeId SMALLINT NOT NULL,
            SearchParamId SMALLINT NOT NULL,
            CodeIdentityId BIGINT NOT NULL,
            ComponentCodeIdentityId BIGINT NOT NULL,
            PRIMARY KEY (ResourceTypeId, SearchParamId, CodeIdentityId)
        );

        INSERT INTO #labels
            (ResourceTypeId, SearchParamId, CodeIdentityId, ComponentCodeIdentityId)
        SELECT DISTINCT
            identityRow.ResourceTypeId,
            identityRow.SearchParamId,
            identityRow.CodeIdentityId,
            identityRow.CodeIdentityId
        FROM dbo.LastNCodeIdentity AS identityRow
        INNER JOIN #affectedComponent AS affectedComponent
            ON affectedComponent.ResourceTypeId = identityRow.ResourceTypeId
            AND affectedComponent.SearchParamId = identityRow.SearchParamId
            AND affectedComponent.ComponentCodeIdentityId = identityRow.ComponentCodeIdentityId;

        INSERT INTO #labels
            (ResourceTypeId, SearchParamId, CodeIdentityId, ComponentCodeIdentityId)
        SELECT
            affectedIdentity.ResourceTypeId,
            affectedIdentity.SearchParamId,
            affectedIdentity.CodeIdentityId,
            affectedIdentity.CodeIdentityId
        FROM #affectedIdentity AS affectedIdentity
        WHERE NOT EXISTS (
            SELECT 1
            FROM #labels AS existingLabel
            WHERE existingLabel.ResourceTypeId = affectedIdentity.ResourceTypeId
                AND existingLabel.SearchParamId = affectedIdentity.SearchParamId
                AND existingLabel.CodeIdentityId = affectedIdentity.CodeIdentityId);

        CREATE TABLE #edgeEndpoints (
            ResourceTypeId SMALLINT NOT NULL,
            SearchParamId SMALLINT NOT NULL,
            CodeIdentityId BIGINT NOT NULL,
            NeighborCodeIdentityId BIGINT NOT NULL,
            PRIMARY KEY (ResourceTypeId, SearchParamId, CodeIdentityId, NeighborCodeIdentityId)
        );

        INSERT INTO #edgeEndpoints
            (ResourceTypeId, SearchParamId, CodeIdentityId, NeighborCodeIdentityId)
        SELECT edge.ResourceTypeId, edge.SearchParamId, edge.LeftCodeIdentityId, edge.RightCodeIdentityId
        FROM dbo.LastNCodeEdge AS edge
        INNER JOIN #labels AS leftLabel
            ON leftLabel.ResourceTypeId = edge.ResourceTypeId
            AND leftLabel.SearchParamId = edge.SearchParamId
            AND leftLabel.CodeIdentityId = edge.LeftCodeIdentityId
        INNER JOIN #labels AS rightLabel
            ON rightLabel.ResourceTypeId = edge.ResourceTypeId
            AND rightLabel.SearchParamId = edge.SearchParamId
            AND rightLabel.CodeIdentityId = edge.RightCodeIdentityId
        UNION ALL
        SELECT edge.ResourceTypeId, edge.SearchParamId, edge.RightCodeIdentityId, edge.LeftCodeIdentityId
        FROM dbo.LastNCodeEdge AS edge
        INNER JOIN #labels AS leftLabel
            ON leftLabel.ResourceTypeId = edge.ResourceTypeId
            AND leftLabel.SearchParamId = edge.SearchParamId
            AND leftLabel.CodeIdentityId = edge.LeftCodeIdentityId
        INNER JOIN #labels AS rightLabel
            ON rightLabel.ResourceTypeId = edge.ResourceTypeId
            AND rightLabel.SearchParamId = edge.SearchParamId
            AND rightLabel.CodeIdentityId = edge.RightCodeIdentityId;

        WHILE 1 = 1
        BEGIN
            UPDATE target
            SET ComponentCodeIdentityId = source.MinimumId
            FROM #labels AS target
            INNER JOIN (
                SELECT
                    endpoint.ResourceTypeId,
                    endpoint.SearchParamId,
                    endpoint.CodeIdentityId,
                    MIN(neighbor.ComponentCodeIdentityId) AS MinimumId
                FROM #edgeEndpoints AS endpoint
                INNER JOIN #labels AS neighbor
                    ON neighbor.ResourceTypeId = endpoint.ResourceTypeId
                    AND neighbor.SearchParamId = endpoint.SearchParamId
                    AND neighbor.CodeIdentityId = endpoint.NeighborCodeIdentityId
                GROUP BY endpoint.ResourceTypeId, endpoint.SearchParamId, endpoint.CodeIdentityId
            ) AS source
                ON source.ResourceTypeId = target.ResourceTypeId
                AND source.SearchParamId = target.SearchParamId
                AND source.CodeIdentityId = target.CodeIdentityId
            WHERE source.MinimumId < target.ComponentCodeIdentityId;

            IF @@ROWCOUNT = 0
                BREAK;
        END;

        UPDATE identityRow
        SET ComponentCodeIdentityId = label.ComponentCodeIdentityId
        FROM dbo.LastNCodeIdentity AS identityRow
        INNER JOIN #labels AS label
            ON label.ResourceTypeId = identityRow.ResourceTypeId
            AND label.SearchParamId = identityRow.SearchParamId
            AND label.CodeIdentityId = identityRow.CodeIdentityId;

        CREATE TABLE #repairedGroup (
            ResourceTypeId SMALLINT NOT NULL,
            SearchParamId SMALLINT NOT NULL,
            ResourceSurrogateId BIGINT NOT NULL,
            CodeGroupId BIGINT NOT NULL,
            PRIMARY KEY (ResourceTypeId, SearchParamId, ResourceSurrogateId)
        );

        INSERT INTO #repairedGroup
            (ResourceTypeId, SearchParamId, ResourceSurrogateId, CodeGroupId)
        SELECT
            membership.ResourceTypeId,
            membership.SearchParamId,
            membership.ResourceSurrogateId,
            MIN(identityRow.ComponentCodeIdentityId)
        FROM dbo.LastNObservationCodeMembership AS membership
        INNER JOIN #labels AS label
            ON label.ResourceTypeId = membership.ResourceTypeId
            AND label.SearchParamId = membership.SearchParamId
            AND label.CodeIdentityId = membership.CodeIdentityId
        INNER JOIN dbo.LastNCodeIdentity AS identityRow
            ON identityRow.CodeIdentityId = membership.CodeIdentityId
            AND identityRow.ResourceTypeId = membership.ResourceTypeId
            AND identityRow.SearchParamId = membership.SearchParamId
        GROUP BY membership.ResourceTypeId, membership.SearchParamId, membership.ResourceSurrogateId;

        UPDATE groupRow
        SET GroupKind = 0,
            CodeGroupId = repairedGroup.CodeGroupId,
            TextCode = NULL
        FROM dbo.LastNObservationCodeGroup AS groupRow
        INNER JOIN #repairedGroup AS repairedGroup
            ON repairedGroup.ResourceTypeId = groupRow.ResourceTypeId
            AND repairedGroup.SearchParamId = groupRow.SearchParamId
            AND repairedGroup.ResourceSurrogateId = groupRow.ResourceSurrogateId;

        INSERT INTO dbo.LastNObservationCodeGroup
            (ResourceTypeId, SearchParamId, ResourceSurrogateId, GroupKind, CodeGroupId, TextCode)
        SELECT
            repairedGroup.ResourceTypeId,
            repairedGroup.SearchParamId,
            repairedGroup.ResourceSurrogateId,
            0,
            repairedGroup.CodeGroupId,
            NULL
        FROM #repairedGroup AS repairedGroup
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.LastNObservationCodeGroup AS groupRow WITH (UPDLOCK, HOLDLOCK)
            WHERE groupRow.ResourceTypeId = repairedGroup.ResourceTypeId
                AND groupRow.SearchParamId = repairedGroup.SearchParamId
                AND groupRow.ResourceSurrogateId = repairedGroup.ResourceSurrogateId);

        IF @InitialTransactionCount = 0
            COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @InitialTransactionCount = 0 AND XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        ELSE IF @InitialTransactionCount > 0 AND XACT_STATE() = 1
            ROLLBACK TRANSACTION MaintainLastNCodeGroups;

        THROW;
    END CATCH;
END;

GO
