CREATE PROCEDURE dbo.PutJobCancelation
@QueueType TINYINT, @GroupId BIGINT=NULL, @JobId BIGINT=NULL
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'PutJobCancelation', @Mode AS VARCHAR (100) = 'Q=' + isnull(CONVERT (VARCHAR, @QueueType), 'NULL') + ' G=' + isnull(CONVERT (VARCHAR, @GroupId), 'NULL') + ' J=' + isnull(CONVERT (VARCHAR, @JobId), 'NULL'), @st AS DATETIME = getUTCdate(), @Rows AS INT, @PartitionId AS TINYINT = @JobId % 16;
BEGIN TRY
    IF @JobId IS NULL
       AND @GroupId IS NULL
        RAISERROR ('@JobId = NULL and @GroupId = NULL', 18, 127);
    IF @JobId IS NOT NULL
        BEGIN
            UPDATE dbo.JobQueue
            SET    Status  = 4,
                   EndDate = getUTCdate(),
                   Version = datediff_big(millisecond, '0001-01-01', getUTCdate())
            WHERE  QueueType = @QueueType
                   AND PartitionId = @PartitionId
                   AND JobId = @JobId
                   AND Status = 0;
            SET @Rows = @@rowcount;
            IF @Rows = 0
                BEGIN
                    UPDATE dbo.JobQueue
                    SET    CancelRequested = 1
                    WHERE  QueueType = @QueueType
                           AND PartitionId = @PartitionId
                           AND JobId = @JobId
                           AND Status = 1;
                    SET @Rows = @@rowcount;
                END
        END
    ELSE
        BEGIN
            UPDATE dbo.JobQueue
            SET    Status  = 4,
                   EndDate = getUTCdate(),
                   Version = datediff_big(millisecond, '0001-01-01', getUTCdate())
            WHERE  QueueType = @QueueType
                   AND GroupId = @GroupId
                   AND Status = 0;
            SET @Rows = @@rowcount;
            UPDATE dbo.JobQueue
            SET    CancelRequested = 1
            WHERE  QueueType = @QueueType
                   AND GroupId = @GroupId
                   AND Status = 1;
            SET @Rows += @@rowcount;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
