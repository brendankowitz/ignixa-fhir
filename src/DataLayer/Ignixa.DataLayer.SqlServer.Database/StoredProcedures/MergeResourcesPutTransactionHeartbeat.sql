CREATE PROCEDURE dbo.MergeResourcesPutTransactionHeartbeat
@TransactionId BIGINT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'MergeResourcesPutTransactionHeartbeat', @Mode AS VARCHAR (100) = 'TR=' + CONVERT (VARCHAR, @TransactionId);
BEGIN TRY
    UPDATE dbo.Transactions
    SET    HeartbeatDate = getUTCdate()
    WHERE  SurrogateIdRangeFirstValue = @TransactionId
           AND IsControlledByClient = 1;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
