CREATE PROCEDURE dbo.DisableIndex
@tableName NVARCHAR (128), @indexName NVARCHAR (128)
WITH EXECUTE AS 'dbo'
AS
DECLARE @errorTxt AS VARCHAR (1000), @sql AS NVARCHAR (1000), @isDisabled AS BIT;
IF object_id(@tableName) IS NULL
    BEGIN
        SET @errorTxt = @tableName + ' does not exist or you don''t have permissions.';
        RAISERROR (@errorTxt, 18, 127);
    END
SET @isDisabled = (SELECT is_disabled
                   FROM   sys.indexes
                   WHERE  object_id = object_id(@tableName)
                          AND name = @indexName);
IF @isDisabled IS NULL
    BEGIN
        SET @errorTxt = @indexName + ' does not exist or you don''t have permissions.';
        RAISERROR (@errorTxt, 18, 127);
    END
IF @isDisabled = 0
    BEGIN
        SET @sql = N'ALTER INDEX ' + QUOTENAME(@indexName) + N' on ' + @tableName + ' Disable';
        EXECUTE sp_executesql @sql;
    END
