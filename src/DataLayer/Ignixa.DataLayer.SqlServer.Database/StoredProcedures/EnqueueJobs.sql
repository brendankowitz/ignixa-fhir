CREATE PROCEDURE dbo.EnqueueJobs
@QueueType TINYINT, @Definitions StringList READONLY, @GroupId BIGINT=NULL, @ForceOneActiveJobGroup BIT=1, @Status TINYINT=NULL, @Result VARCHAR (MAX)=NULL, @StartDate DATETIME=NULL, @ReturnJobs BIT=1
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'EnqueueJobs', @Mode AS VARCHAR (100) = 'Q=' + isnull(CONVERT (VARCHAR, @QueueType), 'NULL') + ' D=' + CONVERT (VARCHAR, (SELECT count(*)
                                                                                                                                                         FROM   @Definitions)) + ' G=' + isnull(CONVERT (VARCHAR, @GroupId), 'NULL') + ' F=' + isnull(CONVERT (VARCHAR, @ForceOneActiveJobGroup), 'NULL') + ' S=' + isnull(CONVERT (VARCHAR, @Status), 'NULL'), @st AS DATETIME = getUTCdate(), @Lock AS VARCHAR (100) = 'EnqueueJobs_' + CONVERT (VARCHAR, @QueueType), @MaxJobId AS BIGINT, @Rows AS INT, @msg AS VARCHAR (1000), @JobIds AS BigintList, @InputRows AS INT;
BEGIN TRY
    DECLARE @Input TABLE (
        DefinitionHash VARBINARY (20) PRIMARY KEY,
        Definition     VARCHAR (MAX) );
    INSERT INTO @Input
    SELECT hashbytes('SHA1', String) AS DefinitionHash,
           String AS Definition
    FROM   @Definitions;
    SET @InputRows = @@rowcount;
    INSERT INTO @JobIds
    SELECT JobId
    FROM   @Input AS A
           INNER JOIN
           dbo.JobQueue AS B
           ON B.QueueType = @QueueType
              AND B.DefinitionHash = A.DefinitionHash
              AND B.Status <> 5;
    IF @@rowcount < @InputRows
        BEGIN
            BEGIN TRANSACTION;
            EXECUTE sp_getapplock @Lock, 'Exclusive';
            IF @ForceOneActiveJobGroup = 1
               AND EXISTS (SELECT *
                           FROM   dbo.JobQueue
                           WHERE  QueueType = @QueueType
                                  AND Status IN (0, 1)
                                  AND (@GroupId IS NULL
                                       OR GroupId <> @GroupId))
                RAISERROR ('There are other active job groups', 18, 127);
            SET @MaxJobId = isnull((SELECT   TOP 1 JobId
                                    FROM     dbo.JobQueue
                                    WHERE    QueueType = @QueueType
                                    ORDER BY JobId DESC), 0);
            INSERT INTO dbo.JobQueue (QueueType, GroupId, JobId, Definition, DefinitionHash, Status, Result, StartDate, EndDate)
            OUTPUT inserted.JobId INTO @JobIds
            SELECT @QueueType,
                   isnull(@GroupId, @MaxJobId + 1) AS GroupId,
                   JobId,
                   Definition,
                   DefinitionHash,
                   isnull(@Status, 0) AS Status,
                   CASE WHEN @Status = 2 THEN @Result ELSE NULL END AS Result,
                   CASE WHEN @Status = 1 THEN getUTCdate() ELSE @StartDate END AS StartDate,
                   CASE WHEN @Status = 2 THEN getUTCdate() ELSE NULL END AS EndDate
            FROM   (SELECT @MaxJobId + row_number() OVER (ORDER BY Dummy) AS JobId,
                           *
                    FROM   (SELECT *,
                                   0 AS Dummy
                            FROM   @Input) AS A) AS A
            WHERE  NOT EXISTS (SELECT *
                               FROM   dbo.JobQueue AS B WITH (INDEX (IX_QueueType_DefinitionHash))
                               WHERE  B.QueueType = @QueueType
                                      AND B.DefinitionHash = A.DefinitionHash
                                      AND B.Status <> 5);
            SET @Rows = @@rowcount;
            COMMIT TRANSACTION;
        END
    IF @ReturnJobs = 1
        EXECUTE dbo.GetJobs @QueueType = @QueueType, @JobIds = @JobIds;
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
