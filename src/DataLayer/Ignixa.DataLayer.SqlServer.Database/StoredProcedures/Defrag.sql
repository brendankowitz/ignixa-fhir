CREATE PROCEDURE dbo.Defrag
@TableName VARCHAR (100), @IndexName VARCHAR (200), @PartitionNumber INT, @IsPartitioned BIT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (200) = @TableName + '.' + @IndexName + '.' + CONVERT (VARCHAR, @PartitionNumber) + '.' + CONVERT (VARCHAR, @IsPartitioned), @st AS DATETIME = getUTCdate(), @SQL AS VARCHAR (3500), @msg AS VARCHAR (1000), @SizeBefore AS FLOAT, @SizeAfter AS FLOAT, @IndexId AS INT, @Operation AS VARCHAR (50) = CASE WHEN EXISTS (SELECT *
                                                                                                                                                                                                                                                                                                                                                                                               FROM   dbo.Parameters
                                                                                                                                                                                                                                                                                                                                                                                               WHERE  Id = 'Defrag.IndexRebuild.IsEnabled'
                                                                                                                                                                                                                                                                                                                                                                                                      AND Number = 1) THEN 'REBUILD' ELSE 'REORGANIZE' END;
SET @Mode = @Mode + ' ' + @Operation;
BEGIN TRY
    SET @IndexId = (SELECT index_id
                    FROM   sys.indexes
                    WHERE  object_id = object_id(@TableName)
                           AND name = @IndexName);
    SET @Sql = 'ALTER INDEX ' + quotename(@IndexName) + ' ON dbo.' + quotename(@TableName) + ' ' + @Operation + CASE WHEN @IsPartitioned = 1 THEN ' PARTITION = ' + CONVERT (VARCHAR, @PartitionNumber) ELSE '' END + CASE WHEN @Operation = 'REBUILD' THEN ' WITH (ONLINE = ON' + CASE WHEN EXISTS (SELECT *
                                                                                                                                                                                                                                                                                                     FROM   sys.partitions
                                                                                                                                                                                                                                                                                                     WHERE  object_id = object_id(@TableName)
                                                                                                                                                                                                                                                                                                            AND index_id = @IndexId
                                                                                                                                                                                                                                                                                                            AND data_compression_desc = 'PAGE') THEN ', DATA_COMPRESSION = PAGE' ELSE '' END + ')' ELSE '' END;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start', @Text = @Sql;
    SET @SizeBefore = (SELECT sum(reserved_page_count)
                       FROM   sys.dm_db_partition_stats
                       WHERE  object_id = object_id(@TableName)
                              AND index_id = @IndexId
                              AND partition_number = @PartitionNumber) * 8.0 / 1024 / 1024;
    SET @msg = 'Size[GB] before=' + CONVERT (VARCHAR, @SizeBefore);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Text = @msg;
    BEGIN TRY
        EXECUTE (@Sql);
        SET @SizeAfter = (SELECT sum(reserved_page_count)
                          FROM   sys.dm_db_partition_stats
                          WHERE  object_id = object_id(@TableName)
                                 AND index_id = @IndexId
                                 AND partition_number = @PartitionNumber) * 8.0 / 1024 / 1024;
        SET @msg = 'Size[GB] before=' + CONVERT (VARCHAR, @SizeBefore) + ', after=' + CONVERT (VARCHAR, @SizeAfter) + ', reduced by=' + CONVERT (VARCHAR, @SizeBefore - @SizeAfter);
        EXECUTE dbo.LogEvent @Process = @SP, @Status = 'End', @Mode = @Mode, @Action = @Operation, @Start = @st, @Text = @msg;
    END TRY
    BEGIN CATCH
        EXECUTE dbo.LogEvent @Process = @SP, @Status = 'Error', @Mode = @Mode, @Action = @Operation, @Start = @st;
        THROW;
    END CATCH
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
