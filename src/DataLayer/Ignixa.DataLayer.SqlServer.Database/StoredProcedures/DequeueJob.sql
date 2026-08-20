CREATE PROCEDURE dbo.DequeueJob
@QueueType TINYINT, @Worker VARCHAR (100), @HeartbeatTimeoutSec INT, @InputJobId BIGINT=NULL, @CheckTimeoutJobs BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'DequeueJob', @Mode AS VARCHAR (100) = 'Q=' + isnull(CONVERT (VARCHAR, @QueueType), 'NULL') + ' H=' + isnull(CONVERT (VARCHAR, @HeartbeatTimeoutSec), 'NULL') + ' W=' + isnull(@Worker, 'NULL') + ' IJ=' + isnull(CONVERT (VARCHAR, @InputJobId), 'NULL') + ' T=' + isnull(CONVERT (VARCHAR, @CheckTimeoutJobs), 'NULL'), @Rows AS INT = 0, @st AS DATETIME = getUTCdate(), @JobId AS BIGINT, @msg AS VARCHAR (100), @Lock AS VARCHAR (100), @PartitionId AS TINYINT, @MaxPartitions AS TINYINT = 16, @LookedAtPartitions AS TINYINT = 0;
BEGIN TRY
    IF EXISTS (SELECT *
               FROM   dbo.Parameters
               WHERE  Id = 'DequeueJobStop'
                      AND Number = 1)
        BEGIN
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = 0, @Text = 'Skipped';
            RETURN;
        END
    IF @InputJobId IS NULL
        SET @PartitionId = @MaxPartitions * rand();
    ELSE
        SET @PartitionId = @InputJobId % 16;
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    WHILE @InputJobId IS NULL
          AND @JobId IS NULL
          AND @LookedAtPartitions < @MaxPartitions
          AND @CheckTimeoutJobs = 0
        BEGIN
            SET @Lock = 'DequeueJob_' + CONVERT (VARCHAR, @QueueType) + '_' + CONVERT (VARCHAR, @PartitionId);
            BEGIN TRANSACTION;
            EXECUTE sp_getapplock @Lock, 'Exclusive';
            UPDATE T
            SET    StartDate     = getUTCdate(),
                   HeartbeatDate = getUTCdate(),
                   Worker        = @Worker,
                   Status        = 1,
                   Version       = datediff_big(millisecond, '0001-01-01', getUTCdate()),
                   @JobId        = T.JobId
            FROM   dbo.JobQueue AS T WITH (PAGLOCK)
                   INNER JOIN
                   (SELECT   TOP 1 JobId
                    FROM     dbo.JobQueue WITH (INDEX (IX_QueueType_PartitionId_Status_Priority))
                    WHERE    QueueType = @QueueType
                             AND PartitionId = @PartitionId
                             AND Status = 0
                    ORDER BY Priority, JobId) AS S
                   ON QueueType = @QueueType
                      AND PartitionId = @PartitionId
                      AND T.JobId = S.JobId;
            SET @Rows += @@rowcount;
            COMMIT TRANSACTION;
            IF @JobId IS NULL
                BEGIN
                    SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END;
                    SET @LookedAtPartitions = @LookedAtPartitions + 1;
                END
        END
    SET @LookedAtPartitions = 0;
    WHILE @InputJobId IS NULL
          AND @JobId IS NULL
          AND @LookedAtPartitions < @MaxPartitions
        BEGIN
            SET @Lock = 'DequeueStoreCopyWorkUnit_' + CONVERT (VARCHAR, @PartitionId);
            BEGIN TRANSACTION;
            EXECUTE sp_getapplock @Lock, 'Exclusive';
            UPDATE T
            SET    StartDate     = getUTCdate(),
                   HeartbeatDate = getUTCdate(),
                   Worker        = @Worker,
                   Status        = CASE WHEN CancelRequested = 0 THEN 1 ELSE 4 END,
                   Version       = datediff_big(millisecond, '0001-01-01', getUTCdate()),
                   @JobId        = CASE WHEN CancelRequested = 0 THEN T.JobId END,
                   Info          = CONVERT (VARCHAR (1000), isnull(Info, '') + ' Prev: Worker=' + Worker + ' Start=' + CONVERT (VARCHAR, StartDate, 121))
            FROM   dbo.JobQueue AS T WITH (PAGLOCK)
                   INNER JOIN
                   (SELECT   TOP 1 JobId
                    FROM     dbo.JobQueue WITH (INDEX (IX_QueueType_PartitionId_Status_Priority))
                    WHERE    QueueType = @QueueType
                             AND PartitionId = @PartitionId
                             AND Status = 1
                             AND datediff(second, HeartbeatDate, getUTCdate()) > @HeartbeatTimeoutSec
                    ORDER BY Priority, JobId) AS S
                   ON QueueType = @QueueType
                      AND PartitionId = @PartitionId
                      AND T.JobId = S.JobId;
            SET @Rows += @@rowcount;
            COMMIT TRANSACTION;
            IF @JobId IS NULL
                BEGIN
                    SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END;
                    SET @LookedAtPartitions = @LookedAtPartitions + 1;
                END
        END
    IF @InputJobId IS NOT NULL
        BEGIN
            UPDATE dbo.JobQueue WITH (PAGLOCK)
            SET    StartDate     = getUTCdate(),
                   HeartbeatDate = getUTCdate(),
                   Worker        = @Worker,
                   Status        = 1,
                   Version       = datediff_big(millisecond, '0001-01-01', getUTCdate()),
                   @JobId        = JobId
            WHERE  QueueType = @QueueType
                   AND PartitionId = @PartitionId
                   AND Status = 0
                   AND JobId = @InputJobId;
            SET @Rows += @@rowcount;
            IF @JobId IS NULL
                BEGIN
                    UPDATE dbo.JobQueue WITH (PAGLOCK)
                    SET    StartDate     = getUTCdate(),
                           HeartbeatDate = getUTCdate(),
                           Worker        = @Worker,
                           Status        = 1,
                           Version       = datediff_big(millisecond, '0001-01-01', getUTCdate()),
                           @JobId        = JobId
                    WHERE  QueueType = @QueueType
                           AND PartitionId = @PartitionId
                           AND Status = 1
                           AND JobId = @InputJobId
                           AND datediff(second, HeartbeatDate, getUTCdate()) > @HeartbeatTimeoutSec;
                    SET @Rows += @@rowcount;
                END
        END
    IF @JobId IS NOT NULL
        EXECUTE dbo.GetJobs @QueueType = @QueueType, @JobId = @JobId;
    SET @msg = 'J=' + isnull(CONVERT (VARCHAR, @JobId), 'NULL') + ' P=' + CONVERT (VARCHAR, @PartitionId);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows, @Text = @msg;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK;
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
