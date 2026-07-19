CREATE PROCEDURE dbo.MergeResourcesPutTransactionInvisibleHistory
@TransactionId BIGINT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (100) = 'TR=' + CONVERT (VARCHAR, @TransactionId), @st AS DATETIME = getUTCdate();
BEGIN TRY
    UPDATE dbo.Transactions
    SET    InvisibleHistoryRemovedDate = getUTCdate()
    WHERE  SurrogateIdRangeFirstValue = @TransactionId
           AND InvisibleHistoryRemovedDate IS NULL;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
