CREATE PROCEDURE dbo.GetResourcesByTransactionId
@TransactionId BIGINT, @IncludeHistory BIT=0, @ReturnResourceKeysOnly BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (100) = 'T=' + CONVERT (VARCHAR, @TransactionId) + ' H=' + CONVERT (VARCHAR, @IncludeHistory), @st AS DATETIME = getUTCdate(), @DummyTop AS BIGINT = 9223372036854775807, @TypeId AS SMALLINT;
BEGIN TRY
    DECLARE @Types TABLE (
        TypeId SMALLINT      PRIMARY KEY,
        Name   VARCHAR (100));
    INSERT INTO @Types
    EXECUTE dbo.GetUsedResourceTypes ;
    DECLARE @Keys TABLE (
        TypeId      SMALLINT,
        SurrogateId BIGINT   PRIMARY KEY (TypeId, SurrogateId));
    WHILE EXISTS (SELECT *
                  FROM   @Types)
        BEGIN
            SET @TypeId = (SELECT   TOP 1 TypeId
                           FROM     @Types
                           ORDER BY TypeId);
            INSERT INTO @Keys
            SELECT @TypeId,
                   ResourceSurrogateId
            FROM   dbo.Resource
            WHERE  ResourceTypeId = @TypeId
                   AND TransactionId = @TransactionId;
            DELETE @Types
            WHERE  TypeId = @TypeId;
        END
    IF @ReturnResourceKeysOnly = 0
        SELECT ResourceTypeId,
               ResourceId,
               ResourceSurrogateId,
               Version,
               IsDeleted,
               IsHistory,
               RawResource,
               IsRawResourceMetaSet,
               SearchParamHash,
               RequestMethod
        FROM   (SELECT TOP (@DummyTop) *
                FROM   @Keys) AS A
               INNER JOIN
               dbo.Resource AS B
               ON ResourceTypeId = TypeId
                  AND ResourceSurrogateId = SurrogateId
        WHERE  IsHistory = 0
               OR @IncludeHistory = 1
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
    ELSE
        SELECT ResourceTypeId,
               ResourceId,
               ResourceSurrogateId,
               Version,
               IsDeleted
        FROM   (SELECT TOP (@DummyTop) *
                FROM   @Keys) AS A
               INNER JOIN
               dbo.Resource AS B
               ON ResourceTypeId = TypeId
                  AND ResourceSurrogateId = SurrogateId
        WHERE  IsHistory = 0
               OR @IncludeHistory = 1
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1));
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
