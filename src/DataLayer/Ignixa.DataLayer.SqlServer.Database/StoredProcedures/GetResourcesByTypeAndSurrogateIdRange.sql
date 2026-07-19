CREATE PROCEDURE dbo.GetResourcesByTypeAndSurrogateIdRange
@ResourceTypeId SMALLINT, @StartId BIGINT, @EndId BIGINT, @GlobalEndId BIGINT=NULL, @IncludeHistory BIT=0, @IncludeDeleted BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetResourcesByTypeAndSurrogateIdRange', @Mode AS VARCHAR (100) = 'RT=' + isnull(CONVERT (VARCHAR, @ResourceTypeId), 'NULL') + ' S=' + isnull(CONVERT (VARCHAR, @StartId), 'NULL') + ' E=' + isnull(CONVERT (VARCHAR, @EndId), 'NULL') + ' GE=' + isnull(CONVERT (VARCHAR, @GlobalEndId), 'NULL') + ' HI=' + isnull(CONVERT (VARCHAR, @IncludeHistory), 'NULL') + ' DE' + isnull(CONVERT (VARCHAR, @IncludeDeleted), 'NULL'), @st AS DATETIME = getUTCdate(), @DummyTop AS BIGINT = 9223372036854775807;
BEGIN TRY
    DECLARE @ResourceIds TABLE (
        ResourceId VARCHAR (64) COLLATE Latin1_General_100_CS_AS PRIMARY KEY);
    DECLARE @SurrogateIds TABLE (
        MaxSurrogateId BIGINT PRIMARY KEY);
    IF @GlobalEndId IS NOT NULL
       AND @IncludeHistory = 0
        BEGIN
            INSERT INTO @ResourceIds
            SELECT DISTINCT ResourceId
            FROM   dbo.Resource
            WHERE  ResourceTypeId = @ResourceTypeId
                   AND ResourceSurrogateId BETWEEN @StartId AND @EndId
                   AND IsHistory = 1
                   AND (IsDeleted = 0
                        OR @IncludeDeleted = 1)
            OPTION (MAXDOP 1);
            IF @@rowcount > 0
                INSERT INTO @SurrogateIds
                SELECT ResourceSurrogateId
                FROM   (SELECT ResourceId,
                               ResourceSurrogateId,
                               row_number() OVER (PARTITION BY ResourceId ORDER BY ResourceSurrogateId DESC) AS RowId
                        FROM   dbo.Resource WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId_Version))
                        WHERE  ResourceTypeId = @ResourceTypeId
                               AND ResourceId IN (SELECT TOP (@DummyTop) ResourceId
                                                  FROM   @ResourceIds)
                               AND ResourceSurrogateId BETWEEN @StartId AND @GlobalEndId) AS A
                WHERE  RowId = 1
                       AND ResourceSurrogateId BETWEEN @StartId AND @EndId
                OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
        END
    SELECT ResourceTypeId,
           ResourceId,
           Version,
           IsDeleted,
           ResourceSurrogateId,
           RequestMethod,
           CONVERT (BIT, 1) AS IsMatch,
           CONVERT (BIT, 0) AS IsPartial,
           IsRawResourceMetaSet,
           SearchParamHash,
           RawResource,
           IsHistory
    FROM   dbo.Resource
    WHERE  ResourceTypeId = @ResourceTypeId
           AND ResourceSurrogateId BETWEEN @StartId AND @EndId
           AND (IsHistory = 0
                OR @IncludeHistory = 1)
           AND (IsDeleted = 0
                OR @IncludeDeleted = 1)
    UNION ALL
    SELECT ResourceTypeId,
           ResourceId,
           Version,
           IsDeleted,
           ResourceSurrogateId,
           RequestMethod,
           CONVERT (BIT, 1) AS IsMatch,
           CONVERT (BIT, 0) AS IsPartial,
           IsRawResourceMetaSet,
           SearchParamHash,
           RawResource,
           IsHistory
    FROM   @SurrogateIds
           INNER JOIN
           dbo.Resource
           ON ResourceTypeId = @ResourceTypeId
              AND ResourceSurrogateId = MaxSurrogateId
    WHERE  IsHistory = 1
           AND (IsDeleted = 0
                OR @IncludeDeleted = 1)
    OPTION (MAXDOP 1);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
