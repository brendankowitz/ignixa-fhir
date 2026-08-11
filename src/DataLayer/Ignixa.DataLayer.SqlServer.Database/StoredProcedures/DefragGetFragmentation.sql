CREATE PROCEDURE dbo.DefragGetFragmentation
@TableName VARCHAR (200), @IndexName VARCHAR (200)=NULL, @PartitionNumber INT=NULL
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @st AS DATETIME = getUTCdate(), @msg AS VARCHAR (1000), @Rows AS INT, @MinFragPct AS INT = isnull((SELECT Number
                                                                                                                                                         FROM   dbo.Parameters
                                                                                                                                                         WHERE  Id = 'Defrag.MinFragPct'), 10), @MinSizeGB AS FLOAT = isnull((SELECT Number
                                                                                                                                                                                                                              FROM   dbo.Parameters
                                                                                                                                                                                                                              WHERE  Id = 'Defrag.MinSizeGB'), 0.1), @PreviousGroupId AS BIGINT, @IndexId AS INT;
DECLARE @Mode AS VARCHAR (200) = 'T=' + @TableName + ' I=' + isnull(@IndexName, 'NULL') + ' P=' + isnull(CONVERT (VARCHAR, @PartitionNumber), 'NULL') + ' MF=' + CONVERT (VARCHAR, @MinFragPct) + ' MS=' + CONVERT (VARCHAR, @MinSizeGB);
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    IF object_id(@TableName) IS NULL
        RAISERROR ('Table does not exist', 18, 127);
    SET @IndexId = (SELECT index_id
                    FROM   sys.indexes
                    WHERE  object_id = object_id(@TableName)
                           AND name = @IndexName);
    IF @IndexName IS NOT NULL
       AND @IndexId IS NULL
        RAISERROR ('Index does not exist', 18, 127);
    SET @PreviousGroupId = (SELECT   TOP 1 GroupId
                            FROM     dbo.JobQueue
                            WHERE    QueueType = 3
                                     AND Status = 5
                                     AND Definition = @TableName
                            ORDER BY GroupId DESC);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = '@PreviousGroupId', @Text = @PreviousGroupId;
    SELECT TableName,
           IndexName,
           partition_number,
           frag_in_percent
    FROM   (SELECT @TableName AS TableName,
                   I.name AS IndexName,
                   partition_number,
                   avg_fragmentation_in_percent AS frag_in_percent,
                   isnull(CONVERT (FLOAT, Result), 0) AS prev_frag_in_percent
            FROM   (SELECT object_id,
                           index_id,
                           partition_number,
                           avg_fragmentation_in_percent
                    FROM   sys.dm_db_index_physical_stats(db_id(), object_id(@TableName), @IndexId, @PartitionNumber, 'LIMITED') AS A
                    WHERE  index_id > 0
                           AND (@PartitionNumber IS NOT NULL
                                OR avg_fragmentation_in_percent >= @MinFragPct
                                   AND A.page_count > @MinSizeGB * 1024 * 1024 / 8)) AS A
                   INNER JOIN
                   sys.indexes AS I
                   ON I.object_id = A.object_id
                      AND I.index_id = A.index_id
                   LEFT OUTER JOIN
                   dbo.JobQueue
                   ON QueueType = 3
                      AND Status = 5
                      AND GroupId = @PreviousGroupId
                      AND Definition = I.name + ';' + CONVERT (VARCHAR, partition_number)) AS A
    WHERE  @PartitionNumber IS NOT NULL
           OR frag_in_percent >= prev_frag_in_percent + @MinFragPct;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
