CREATE PROCEDURE dbo.ArchiveJobs
@QueueType TINYINT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'ArchiveJobs', @Mode AS VARCHAR (100) = '', @st AS DATETIME = getUTCdate(), @Rows AS INT = 0, @PartitionId AS TINYINT, @MaxPartitions AS TINYINT = 16, @LookedAtPartitions AS TINYINT = 0, @InflightRows AS INT = 0, @Lock AS VARCHAR (100) = 'DequeueJob_' + CONVERT (VARCHAR, @QueueType);
BEGIN TRY
    SET @PartitionId = @MaxPartitions * rand();
    BEGIN TRANSACTION;
    EXECUTE sp_getapplock @Lock, 'Exclusive';
    WHILE @LookedAtPartitions <= @MaxPartitions
        BEGIN
            SET @InflightRows += (SELECT count(*)
                                  FROM   dbo.JobQueue
                                  WHERE  PartitionId = @PartitionId
                                         AND QueueType = @QueueType
                                         AND Status IN (0, 1));
            SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END;
            SET @LookedAtPartitions = @LookedAtPartitions + 1;
        END
    IF @InflightRows = 0
        BEGIN
            SET @LookedAtPartitions = 0;
            WHILE @LookedAtPartitions <= @MaxPartitions
                BEGIN
                    UPDATE dbo.JobQueue
                    SET    Status = 5
                    WHERE  PartitionId = @PartitionId
                           AND QueueType = @QueueType
                           AND Status IN (2, 3, 4);
                    SET @Rows += @@rowcount;
                    SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END;
                    SET @LookedAtPartitions = @LookedAtPartitions + 1;
                END
        END
    COMMIT TRANSACTION;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK;
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
