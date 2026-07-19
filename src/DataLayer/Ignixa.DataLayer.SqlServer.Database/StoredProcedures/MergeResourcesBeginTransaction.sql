CREATE PROCEDURE dbo.MergeResourcesBeginTransaction
@Count INT, @TransactionId BIGINT OUTPUT, @SequenceRangeFirstValue INT=NULL OUTPUT, @HeartbeatDate DATETIME=NULL, @EnableThrottling BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'MergeResourcesBeginTransaction', @Mode AS VARCHAR (200) = 'Cnt=' + CONVERT (VARCHAR, @Count) + ' HB=' + isnull(CONVERT (VARCHAR, @HeartbeatDate, 121), 'NULL') + ' ET=' + CONVERT (VARCHAR, @EnableThrottling), @st AS DATETIME = getUTCdate(), @FirstValueVar AS SQL_VARIANT, @LastValueVar AS SQL_VARIANT, @OptimalConcurrency AS INT = isnull((SELECT Number
                                                                                                                                                                                                                                                                                                                                                                                  FROM   Parameters
                                                                                                                                                                                                                                                                                                                                                                                  WHERE  Id = 'MergeResources.OptimalConcurrentCalls'), 256), @CurrentConcurrency AS INT, @msg AS VARCHAR (1000);
BEGIN TRY
    SET @TransactionId = NULL;
    IF @@trancount > 0
        RAISERROR ('MergeResourcesBeginTransaction cannot be called inside outer transaction.', 18, 127);
    IF @EnableThrottling = 1
        BEGIN
            SET @CurrentConcurrency = (SELECT count(*)
                                       FROM   sys.dm_exec_sessions
                                       WHERE  status <> 'sleeping'
                                              AND program_name = 'MergeResources');
            IF @CurrentConcurrency > @OptimalConcurrency
                BEGIN
                    SET @msg = 'Number of concurrent MergeResources calls = ' + CONVERT (VARCHAR, @CurrentConcurrency) + ' is above optimal = ' + CONVERT (VARCHAR, @OptimalConcurrency) + '.';
                    THROW 50410, @msg, 1;
                END
        END
    SET @FirstValueVar = NULL;
    WHILE @FirstValueVar IS NULL
        BEGIN
            EXECUTE sys.sp_sequence_get_range @sequence_name = 'dbo.ResourceSurrogateIdUniquifierSequence', @range_size = @Count, @range_first_value = @FirstValueVar OUTPUT, @range_last_value = @LastValueVar OUTPUT;
            SET @SequenceRangeFirstValue = CONVERT (INT, @FirstValueVar);
            IF @SequenceRangeFirstValue > CONVERT (INT, @LastValueVar)
                SET @FirstValueVar = NULL;
        END
    SET @TransactionId = datediff_big(millisecond, '0001-01-01', sysUTCdatetime()) * 80000 + @SequenceRangeFirstValue;
    INSERT INTO dbo.Transactions (SurrogateIdRangeFirstValue, SurrogateIdRangeLastValue, HeartbeatDate)
    SELECT @TransactionId,
           @TransactionId + @Count - 1,
           isnull(@HeartbeatDate, getUTCdate());
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    IF @@trancount > 0
        ROLLBACK;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
