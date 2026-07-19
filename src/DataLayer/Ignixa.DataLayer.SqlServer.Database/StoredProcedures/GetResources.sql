CREATE PROCEDURE dbo.GetResources
@ResourceKeys dbo.ResourceKeyList READONLY
AS
SET NOCOUNT ON;
DECLARE @st AS DATETIME = getUTCdate(), @SP AS VARCHAR (100) = 'GetResources', @InputRows AS INT, @DummyTop AS BIGINT = 9223372036854775807, @NotNullVersionExists AS BIT, @NullVersionExists AS BIT, @MinRT AS SMALLINT, @MaxRT AS SMALLINT;
SELECT @MinRT = min(ResourceTypeId),
       @MaxRT = max(ResourceTypeId),
       @InputRows = count(*),
       @NotNullVersionExists = max(CASE WHEN Version IS NOT NULL THEN 1 ELSE 0 END),
       @NullVersionExists = max(CASE WHEN Version IS NULL THEN 1 ELSE 0 END)
FROM   @ResourceKeys;
DECLARE @Mode AS VARCHAR (100) = 'RT=[' + CONVERT (VARCHAR, @MinRT) + ',' + CONVERT (VARCHAR, @MaxRT) + '] Cnt=' + CONVERT (VARCHAR, @InputRows) + ' NNVE=' + CONVERT (VARCHAR, @NotNullVersionExists) + ' NVE=' + CONVERT (VARCHAR, @NullVersionExists);
BEGIN TRY
    IF @NotNullVersionExists = 1
        IF @NullVersionExists = 0
            SELECT B.ResourceTypeId,
                   B.ResourceId,
                   ResourceSurrogateId,
                   B.Version,
                   IsDeleted,
                   IsHistory,
                   RawResource,
                   IsRawResourceMetaSet,
                   SearchParamHash
            FROM   (SELECT TOP (@DummyTop) *
                    FROM   @ResourceKeys) AS A
                   INNER JOIN
                   dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId_Version))
                   ON B.ResourceTypeId = A.ResourceTypeId
                      AND B.ResourceId = A.ResourceId
                      AND B.Version = A.Version
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
        ELSE
            SELECT *
            FROM   (SELECT B.ResourceTypeId,
                           B.ResourceId,
                           ResourceSurrogateId,
                           B.Version,
                           IsDeleted,
                           IsHistory,
                           RawResource,
                           IsRawResourceMetaSet,
                           SearchParamHash
                    FROM   (SELECT TOP (@DummyTop) *
                            FROM   @ResourceKeys
                            WHERE  Version IS NOT NULL) AS A
                           INNER JOIN
                           dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId_Version))
                           ON B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceId = A.ResourceId
                              AND B.Version = A.Version
                    UNION ALL
                    SELECT B.ResourceTypeId,
                           B.ResourceId,
                           ResourceSurrogateId,
                           B.Version,
                           IsDeleted,
                           IsHistory,
                           RawResource,
                           IsRawResourceMetaSet,
                           SearchParamHash
                    FROM   (SELECT TOP (@DummyTop) *
                            FROM   @ResourceKeys
                            WHERE  Version IS NULL) AS A
                           INNER JOIN
                           dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId))
                           ON B.ResourceTypeId = A.ResourceTypeId
                              AND B.ResourceId = A.ResourceId
                    WHERE  IsHistory = 0) AS A
            OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
    ELSE
        SELECT B.ResourceTypeId,
               B.ResourceId,
               ResourceSurrogateId,
               B.Version,
               IsDeleted,
               IsHistory,
               RawResource,
               IsRawResourceMetaSet,
               SearchParamHash
        FROM   (SELECT TOP (@DummyTop) *
                FROM   @ResourceKeys) AS A
               INNER JOIN
               dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId))
               ON B.ResourceTypeId = A.ResourceTypeId
                  AND B.ResourceId = A.ResourceId
        WHERE  IsHistory = 0
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH
