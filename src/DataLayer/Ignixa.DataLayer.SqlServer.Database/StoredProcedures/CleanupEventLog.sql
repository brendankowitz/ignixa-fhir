CREATE PROCEDURE dbo.CleanupEventLog
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'CleanupEventLog', @Mode AS VARCHAR (100) = '', @MaxDeleteRows AS INT, @MaxAllowedRows AS BIGINT, @RetentionPeriodSecond AS INT, @DeletedRows AS INT, @TotalDeletedRows AS INT = 0, @TotalRows AS INT, @Now AS DATETIME = getUTCdate();
EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
BEGIN TRY
    SET @MaxDeleteRows = (SELECT Number
                          FROM   dbo.Parameters
                          WHERE  Id = 'CleanupEventLog.DeleteBatchSize');
    IF @MaxDeleteRows IS NULL
        RAISERROR ('Cannot get Parameter.CleanupEventLog.DeleteBatchSize', 18, 127);
    SET @MaxAllowedRows = (SELECT Number
                           FROM   dbo.Parameters
                           WHERE  Id = 'CleanupEventLog.AllowedRows');
    IF @MaxAllowedRows IS NULL
        RAISERROR ('Cannot get Parameter.CleanupEventLog.AllowedRows', 18, 127);
    SET @RetentionPeriodSecond = (SELECT Number * 24 * 60 * 60
                                  FROM   dbo.Parameters
                                  WHERE  Id = 'CleanupEventLog.RetentionPeriodDay');
    IF @RetentionPeriodSecond IS NULL
        RAISERROR ('Cannot get Parameter.CleanupEventLog.RetentionPeriodDay', 18, 127);
    SET @TotalRows = (SELECT sum(row_count)
                      FROM   sys.dm_db_partition_stats
                      WHERE  object_id = object_id('EventLog')
                             AND index_id IN (0, 1));
    SET @DeletedRows = 1;
    WHILE @DeletedRows > 0
          AND EXISTS (SELECT *
                      FROM   dbo.Parameters
                      WHERE  Id = 'CleanupEventLog.IsEnabled'
                             AND Number = 1)
        BEGIN
            SET @DeletedRows = 0;
            IF @TotalRows - @TotalDeletedRows > @MaxAllowedRows
                BEGIN
                    DELETE TOP (@MaxDeleteRows)
                           dbo.EventLog WITH (PAGLOCK)
                    WHERE  EventDate <= dateadd(second, -@RetentionPeriodSecond, @Now);
                    SET @DeletedRows = @@rowcount;
                    SET @TotalDeletedRows += @DeletedRows;
                    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = 'EventLog', @Action = 'Delete', @Rows = @DeletedRows, @Text = @TotalDeletedRows;
                END
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @Now;
END TRY
BEGIN CATCH
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
