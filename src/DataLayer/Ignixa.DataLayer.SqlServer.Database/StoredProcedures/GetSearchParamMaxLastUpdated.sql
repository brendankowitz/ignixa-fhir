CREATE PROCEDURE dbo.GetSearchParamMaxLastUpdated
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (200) = 'SearchParam MaxLastUpdated Query', @st AS DATETIME = getUTCdate(), @MaxLastUpdated AS DATETIMEOFFSET (7);
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start', @Start = @st;
    SELECT @MaxLastUpdated = MAX(LastUpdated)
    FROM   dbo.SearchParam;
    SELECT @MaxLastUpdated AS MaxLastUpdated;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@ROWCOUNT;
END TRY
BEGIN CATCH
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH
