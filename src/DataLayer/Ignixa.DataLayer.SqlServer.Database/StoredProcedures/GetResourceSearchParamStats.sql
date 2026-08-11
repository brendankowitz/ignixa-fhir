CREATE PROCEDURE dbo.GetResourceSearchParamStats
@Table VARCHAR (100)=NULL, @ResourceTypeId SMALLINT=NULL, @SearchParamId SMALLINT=NULL
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (200) = 'T=' + isnull(@Table, 'NULL') + ' RT=' + isnull(CONVERT (VARCHAR, @ResourceTypeId), 'NULL') + ' SP=' + isnull(CONVERT (VARCHAR, @SearchParamId), 'NULL'), @st AS DATETIME = getUTCdate();
BEGIN TRY
    SELECT T.name AS TableName,
           S.name AS StatsName,
           db_name() AS DatabaseName
    FROM   sys.stats AS S
           INNER JOIN
           sys.tables AS T
           ON T.object_id = S.object_id
    WHERE  T.name LIKE '%SearchParam'
           AND T.name <> 'SearchParam'
           AND S.name LIKE 'ST[_]%'
           AND (T.name LIKE @Table
                OR @Table IS NULL)
           AND (S.name LIKE '%ResourceTypeId[_]' + CONVERT (VARCHAR, @ResourceTypeId) + '[_]%'
                OR @ResourceTypeId IS NULL)
           AND (S.name LIKE '%SearchParamId[_]' + CONVERT (VARCHAR, @SearchParamId)
                OR @SearchParamId IS NULL);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Rows = @@rowcount, @Start = @st;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH
