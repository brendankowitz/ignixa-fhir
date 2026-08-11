CREATE PROCEDURE dbo.PutJobStatus
@QueueType TINYINT, @JobId BIGINT, @Version BIGINT, @Failed BIT, @Data BIGINT, @FinalResult VARCHAR (MAX), @RequestCancellationOnFailure BIT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'PutJobStatus', @Mode AS VARCHAR (100), @st AS DATETIME = getUTCdate(), @Rows AS INT = 0, @PartitionId AS TINYINT = @JobId % 16, @GroupId AS BIGINT;
SET @Mode = 'Q=' + CONVERT (VARCHAR, @QueueType) + ' J=' + CONVERT (VARCHAR, @JobId) + ' P=' + CONVERT (VARCHAR, @PartitionId) + ' V=' + CONVERT (VARCHAR, @Version) + ' F=' + CONVERT (VARCHAR, @Failed) + ' R=' + isnull(@FinalResult, 'NULL');
BEGIN TRY
    UPDATE dbo.JobQueue
    SET    EndDate  = getUTCdate(),
           Status   = CASE WHEN @Failed = 1 THEN 3 WHEN CancelRequested = 1 THEN 4 ELSE 2 END,
           Data     = @Data,
           Result   = @FinalResult,
           @GroupId = GroupId
    WHERE  QueueType = @QueueType
           AND PartitionId = @PartitionId
           AND JobId = @JobId
           AND Status = 1
           AND Version = @Version;
    SET @Rows = @@rowcount;
    IF @Rows = 0
        BEGIN
            SET @GroupId = (SELECT GroupId
                            FROM   dbo.JobQueue
                            WHERE  QueueType = @QueueType
                                   AND PartitionId = @PartitionId
                                   AND JobId = @JobId
                                   AND Version = @Version
                                   AND Status IN (2, 3, 4));
            IF @GroupId IS NULL
                IF EXISTS (SELECT *
                           FROM   dbo.JobQueue
                           WHERE  QueueType = @QueueType
                                  AND PartitionId = @PartitionId
                                  AND JobId = @JobId)
                    THROW 50412, 'Precondition failed', 1;
                ELSE
                    THROW 50404, 'Job record not found', 1;
        END
    IF @Failed = 1
       AND @RequestCancellationOnFailure = 1
        EXECUTE dbo.PutJobCancelation @QueueType = @QueueType, @GroupId = @GroupId;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
