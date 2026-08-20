CREATE PROCEDURE dbo.MergeResourcesCommitTransaction
@TransactionId BIGINT, @FailureReason VARCHAR (MAX)=NULL, @OverrideIsControlledByClientCheck BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'MergeResourcesCommitTransaction', @st AS DATETIME = getUTCdate(), @InitialTranCount AS INT = @@trancount, @IsCompletedBefore AS BIT, @Rows AS INT, @msg AS VARCHAR (1000);
DECLARE @Mode AS VARCHAR (200) = 'TR=' + CONVERT (VARCHAR, @TransactionId) + ' OC=' + isnull(CONVERT (VARCHAR, @OverrideIsControlledByClientCheck), 'NULL');
BEGIN TRY
    IF @InitialTranCount = 0
        BEGIN TRANSACTION;
    UPDATE dbo.Transactions
    SET    IsCompleted        = 1,
           @IsCompletedBefore = IsCompleted,
           EndDate            = getUTCdate(),
           IsSuccess          = CASE WHEN @FailureReason IS NULL THEN 1 ELSE 0 END,
           FailureReason      = @FailureReason
    WHERE  SurrogateIdRangeFirstValue = @TransactionId
           AND (IsControlledByClient = 1
                OR @OverrideIsControlledByClientCheck = 1);
    SET @Rows = @@rowcount;
    IF @Rows = 0
        BEGIN
            SET @msg = 'Transaction [' + CONVERT (VARCHAR (20), @TransactionId) + '] is not controlled by client or does not exist.';
            RAISERROR (@msg, 18, 127);
        END
    IF @IsCompletedBefore = 1
        BEGIN
            IF @InitialTranCount = 0
                ROLLBACK;
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows, @Target = '@IsCompletedBefore', @Text = '=1';
            RETURN;
        END
    IF @InitialTranCount = 0
        COMMIT TRANSACTION;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF @InitialTranCount = 0
       AND @@trancount > 0
        ROLLBACK;
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
