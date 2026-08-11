CREATE PROCEDURE dbo.LogEvent
@Process VARCHAR (100), @Status VARCHAR (10), @Mode VARCHAR (200)=NULL, @Action VARCHAR (20)=NULL, @Target VARCHAR (100)=NULL, @Rows BIGINT=NULL, @Start DATETIME=NULL, @Text NVARCHAR (3500)=NULL, @EventId BIGINT=NULL OUTPUT, @Retry INT=NULL
AS
SET NOCOUNT ON;
DECLARE @ErrorNumber AS INT = error_number(), @ErrorMessage AS VARCHAR (1000) = '', @TranCount AS INT = @@trancount, @DoWork AS BIT = 0, @NumberAdded AS BIT;
IF @ErrorNumber IS NOT NULL
   OR @Status IN ('Warn', 'Error')
    SET @DoWork = 1;
IF @DoWork = 0
    SET @DoWork = CASE WHEN EXISTS (SELECT *
                                    FROM   dbo.Parameters
                                    WHERE  Id = isnull(@Process, '')
                                           AND Char = 'LogEvent') THEN 1 ELSE 0 END;
IF @DoWork = 0
    RETURN;
IF @ErrorNumber IS NOT NULL
    SET @ErrorMessage = CASE WHEN @Retry IS NOT NULL THEN 'Retry ' + CONVERT (VARCHAR, @Retry) + ', ' ELSE '' END + 'Error ' + CONVERT (VARCHAR, error_number()) + ': ' + CONVERT (VARCHAR (1000), error_message()) + ', Level ' + CONVERT (VARCHAR, error_severity()) + ', State ' + CONVERT (VARCHAR, error_state()) + CASE WHEN error_procedure() IS NOT NULL THEN ', Procedure ' + error_procedure() ELSE '' END + ', Line ' + CONVERT (VARCHAR, error_line());
IF @TranCount > 0
   AND @ErrorNumber IS NOT NULL
    ROLLBACK;
IF databasepropertyex(db_name(), 'UpdateAbility') = 'READ_WRITE'
    BEGIN
        INSERT INTO dbo.EventLog (Process, Status, Mode, Action, Target, Rows, Milliseconds, EventDate, EventText, SPID, HostName)
        SELECT @Process,
               @Status,
               @Mode,
               @Action,
               @Target,
               @Rows,
               datediff(millisecond, @Start, getUTCdate()),
               getUTCdate() AS EventDate,
               CASE WHEN @ErrorNumber IS NULL THEN @Text ELSE @ErrorMessage + CASE WHEN isnull(@Text, '') <> '' THEN '. ' + @Text ELSE '' END END AS Text,
               @@SPID,
               host_name() AS HostName;
        SET @EventId = scope_identity();
    END
IF @TranCount > 0
   AND @ErrorNumber IS NOT NULL
    BEGIN TRANSACTION;
