CREATE PROCEDURE dbo.GetResourceVersions
@ResourceDateKeys dbo.ResourceDateKeyList READONLY
AS
SET NOCOUNT ON;
DECLARE @st AS DATETIME = getUTCdate(), @SP AS VARCHAR (100) = 'GetResourceVersions', @Mode AS VARCHAR (100) = 'Rows=' + CONVERT (VARCHAR, (SELECT count(*)
                                                                                                                                            FROM   @ResourceDateKeys)), @DummyTop AS BIGINT = 9223372036854775807;
BEGIN TRY
    SELECT A.ResourceTypeId,
           A.ResourceId,
           A.ResourceSurrogateId,
           CASE WHEN D.Version IS NOT NULL THEN 0 WHEN isnull(U.Version, 1) - isnull(L.Version, 0) > ResourceIndex THEN isnull(U.Version, 1) - ResourceIndex ELSE isnull(M.Version, 0) - ResourceIndex END AS Version,
           isnull(D.Version, 0) AS MatchedVersion,
           D.RawResource AS MatchedRawResource
    FROM   (SELECT TOP (@DummyTop) *,
                                   CONVERT (INT, row_number() OVER (PARTITION BY ResourceTypeId, ResourceId ORDER BY ResourceSurrogateId DESC)) AS ResourceIndex
            FROM   @ResourceDateKeys) AS A OUTER APPLY (SELECT   TOP 1 *
                                                        FROM     dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId_Version))
                                                        WHERE    B.ResourceTypeId = A.ResourceTypeId
                                                                 AND B.ResourceId = A.ResourceId
                                                                 AND B.Version > 0
                                                                 AND B.ResourceSurrogateId < A.ResourceSurrogateId
                                                        ORDER BY B.ResourceSurrogateId DESC) AS L OUTER APPLY (SELECT   TOP 1 *
                                                                                                               FROM     dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId_Version))
                                                                                                               WHERE    B.ResourceTypeId = A.ResourceTypeId
                                                                                                                        AND B.ResourceId = A.ResourceId
                                                                                                                        AND B.Version > 0
                                                                                                                        AND B.ResourceSurrogateId > A.ResourceSurrogateId
                                                                                                               ORDER BY B.ResourceSurrogateId) AS U OUTER APPLY (SELECT   TOP 1 *
                                                                                                                                                                 FROM     dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId_Version))
                                                                                                                                                                 WHERE    B.ResourceTypeId = A.ResourceTypeId
                                                                                                                                                                          AND B.ResourceId = A.ResourceId
                                                                                                                                                                          AND B.Version < 0
                                                                                                                                                                 ORDER BY B.Version) AS M OUTER APPLY (SELECT TOP 1 *
                                                                                                                                                                                                       FROM   dbo.Resource AS B WITH (INDEX (IX_Resource_ResourceTypeId_ResourceId_Version))
                                                                                                                                                                                                       WHERE  B.ResourceTypeId = A.ResourceTypeId
                                                                                                                                                                                                              AND B.ResourceId = A.ResourceId
                                                                                                                                                                                                              AND B.ResourceSurrogateId BETWEEN A.ResourceSurrogateId AND A.ResourceSurrogateId + 79999) AS D
    OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH
