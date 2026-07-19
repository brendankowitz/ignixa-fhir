CREATE PROCEDURE dbo.MergeResourcesDeleteInvisibleHistory
@TransactionId BIGINT, @AffectedRows INT=NULL OUTPUT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (100) = 'T=' + CONVERT (VARCHAR, @TransactionId), @st AS DATETIME = getUTCdate(), @TypeId AS SMALLINT;
SET @AffectedRows = 0;
BEGIN TRY
    DECLARE @Types TABLE (
        TypeId SMALLINT      PRIMARY KEY,
        Name   VARCHAR (100));
    INSERT INTO @Types
    EXECUTE dbo.GetUsedResourceTypes ;
    WHILE EXISTS (SELECT *
                  FROM   @Types)
        BEGIN
            SET @TypeId = (SELECT   TOP 1 TypeId
                           FROM     @Types
                           ORDER BY TypeId);
            DELETE dbo.Resource
            WHERE  ResourceTypeId = @TypeId
                   AND HistoryTransactionId = @TransactionId
                   AND RawResource = 0xF;
            SET @AffectedRows += @@rowcount;
            DELETE @Types
            WHERE  TypeId = @TypeId;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @AffectedRows;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
