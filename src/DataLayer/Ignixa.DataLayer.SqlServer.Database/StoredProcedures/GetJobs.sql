CREATE PROCEDURE dbo.GetJobs
@QueueType TINYINT, @JobId BIGINT=NULL, @JobIds BigintList READONLY, @GroupId BIGINT=NULL, @ReturnDefinition BIT=1
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetJobs', @Mode AS VARCHAR (100) = 'Q=' + isnull(CONVERT (VARCHAR, @QueueType), 'NULL') + ' J=' + isnull(CONVERT (VARCHAR, @JobId), 'NULL') + ' G=' + isnull(CONVERT (VARCHAR, @GroupId), 'NULL'), @st AS DATETIME = getUTCdate(), @PartitionId AS TINYINT = @JobId % 16;
BEGIN TRY
    IF @JobId IS NULL
       AND @GroupId IS NULL
       AND NOT EXISTS (SELECT *
                       FROM   @JobIds)
        RAISERROR ('@JobId = NULL and @GroupId = NULL and @JobIds is empty', 18, 127);
    IF @JobId IS NOT NULL
        SELECT GroupId,
               JobId,
               CASE WHEN @ReturnDefinition = 1 THEN Definition ELSE NULL END AS Definition,
               Version,
               Status,
               Priority,
               Data,
               Result,
               CreateDate,
               StartDate,
               EndDate,
               HeartbeatDate,
               CancelRequested
        FROM   dbo.JobQueue
        WHERE  QueueType = @QueueType
               AND PartitionId = @PartitionId
               AND JobId = isnull(@JobId, -1)
               AND Status <> 5;
    ELSE
        IF @GroupId IS NOT NULL
            SELECT GroupId,
                   JobId,
                   CASE WHEN @ReturnDefinition = 1 THEN Definition ELSE NULL END AS Definition,
                   Version,
                   Status,
                   Priority,
                   Data,
                   Result,
                   CreateDate,
                   StartDate,
                   EndDate,
                   HeartbeatDate,
                   CancelRequested
            FROM   dbo.JobQueue WITH (INDEX (IX_QueueType_GroupId))
            WHERE  QueueType = @QueueType
                   AND GroupId = isnull(@GroupId, -1)
                   AND Status <> 5;
        ELSE
            SELECT GroupId,
                   JobId,
                   CASE WHEN @ReturnDefinition = 1 THEN Definition ELSE NULL END AS Definition,
                   Version,
                   Status,
                   Priority,
                   Data,
                   Result,
                   CreateDate,
                   StartDate,
                   EndDate,
                   HeartbeatDate,
                   CancelRequested
            FROM   dbo.JobQueue
            WHERE  QueueType = @QueueType
                   AND JobId IN (SELECT Id
                                 FROM   @JobIds)
                   AND PartitionId = JobId % 16
                   AND Status <> 5;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
