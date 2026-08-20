CREATE PROCEDURE dbo.MergeResourcesGetTimeoutTransactions
@TimeoutSec INT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (100) = 'T=' + CONVERT (VARCHAR, @TimeoutSec), @st AS DATETIME = getUTCdate(), @MinTransactionId AS BIGINT;
BEGIN TRY
    EXECUTE dbo.MergeResourcesGetTransactionVisibility @MinTransactionId OUTPUT;
    SELECT   SurrogateIdRangeFirstValue
    FROM     dbo.Transactions
    WHERE    SurrogateIdRangeFirstValue > @MinTransactionId
             AND IsCompleted = 0
             AND datediff(second, HeartbeatDate, getUTCdate()) > @TimeoutSec
    ORDER BY SurrogateIdRangeFirstValue;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
