CREATE PROCEDURE dbo.GetResourceSurrogateIdRanges
@ResourceTypeId SMALLINT, @StartId BIGINT, @EndId BIGINT, @RangeSize INT, @NumberOfRanges INT=100, @Up BIT=1, @ActiveOnly BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetResourceSurrogateIdRanges', @Mode AS VARCHAR (100) = 'RT=' + isnull(CONVERT (VARCHAR, @ResourceTypeId), 'NULL') + ' S=' + isnull(CONVERT (VARCHAR, @StartId), 'NULL') + ' E=' + isnull(CONVERT (VARCHAR, @EndId), 'NULL') + ' R=' + isnull(CONVERT (VARCHAR, @RangeSize), 'NULL') + ' UP=' + isnull(CONVERT (VARCHAR, @Up), 'NULL') + ' AO=' + isnull(CONVERT (VARCHAR, @ActiveOnly), 'NULL'), @st AS DATETIME = getUTCdate();
BEGIN TRY
    IF @Up = 1
        SELECT   RangeId,
                 min(ResourceSurrogateId),
                 max(ResourceSurrogateId),
                 count(*)
        FROM     (SELECT isnull(CONVERT (INT, (row_number() OVER (ORDER BY ResourceSurrogateId) - 1) / @RangeSize), 0) AS RangeId,
                         ResourceSurrogateId
                  FROM   (SELECT   TOP (@RangeSize * @NumberOfRanges) ResourceSurrogateId
                          FROM     dbo.Resource
                          WHERE    ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId >= @StartId
                                   AND ResourceSurrogateId <= @EndId
                                   AND (@ActiveOnly = 0
                                        OR (IsHistory = 0
                                            AND IsDeleted = 0))
                          ORDER BY ResourceSurrogateId) AS A) AS A
        GROUP BY RangeId
        OPTION (MAXDOP 1);
    ELSE
        SELECT   RangeId,
                 min(ResourceSurrogateId),
                 max(ResourceSurrogateId),
                 count(*)
        FROM     (SELECT isnull(CONVERT (INT, (row_number() OVER (ORDER BY ResourceSurrogateId) - 1) / @RangeSize), 0) AS RangeId,
                         ResourceSurrogateId
                  FROM   (SELECT   TOP (@RangeSize * @NumberOfRanges) ResourceSurrogateId
                          FROM     dbo.Resource
                          WHERE    ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId >= @StartId
                                   AND ResourceSurrogateId <= @EndId
                                   AND (@ActiveOnly = 0
                                        OR (IsHistory = 0
                                            AND IsDeleted = 0))
                          ORDER BY ResourceSurrogateId DESC) AS A) AS A
        GROUP BY RangeId
        OPTION (MAXDOP 1);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
