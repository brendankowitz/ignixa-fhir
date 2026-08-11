CREATE PROCEDURE dbo.DefragChangeDatabaseSettings
@IsOn BIT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'DefragChangeDatabaseSettings', @Mode AS VARCHAR (200) = 'On=' + CONVERT (VARCHAR, @IsOn), @st AS DATETIME = getUTCdate(), @SQL AS VARCHAR (3500);
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Status = 'Start', @Mode = @Mode;
    SET @SQL = 'ALTER DATABASE CURRENT SET AUTO_UPDATE_STATISTICS ' + CASE WHEN @IsOn = 1 THEN 'ON' ELSE 'OFF' END;
    EXECUTE (@SQL);
    EXECUTE dbo.LogEvent @Process = @SP, @Status = 'Run', @Mode = @Mode, @Text = @SQL;
    SET @SQL = 'ALTER DATABASE CURRENT SET AUTO_CREATE_STATISTICS ' + CASE WHEN @IsOn = 1 THEN 'ON' ELSE 'OFF' END;
    EXECUTE (@SQL);
    EXECUTE dbo.LogEvent @Process = @SP, @Status = 'End', @Mode = @Mode, @Start = @st, @Text = @SQL;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
