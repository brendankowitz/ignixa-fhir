CREATE PROCEDURE dbo.MergeResourcesAdvanceTransactionVisibility
@AffectedRows INT=0 OUTPUT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (100) = '', @st AS DATETIME = getUTCdate(), @msg AS VARCHAR (1000), @MaxTransactionId AS BIGINT, @MinTransactionId AS BIGINT, @MinNotCompletedTransactionId AS BIGINT, @CurrentTransactionId AS BIGINT;
SET @AffectedRows = 0;
BEGIN TRY
    EXECUTE dbo.MergeResourcesGetTransactionVisibility @MinTransactionId OUTPUT;
    SET @MinTransactionId += 1;
    SET @CurrentTransactionId = (SELECT   TOP 1 SurrogateIdRangeFirstValue
                                 FROM     dbo.Transactions
                                 ORDER BY SurrogateIdRangeFirstValue DESC);
    SET @MinNotCompletedTransactionId = isnull((SELECT   TOP 1 SurrogateIdRangeFirstValue
                                                FROM     dbo.Transactions
                                                WHERE    IsCompleted = 0
                                                         AND SurrogateIdRangeFirstValue BETWEEN @MinTransactionId AND @CurrentTransactionId
                                                ORDER BY SurrogateIdRangeFirstValue), @CurrentTransactionId + 1);
    SET @MaxTransactionId = (SELECT   TOP 1 SurrogateIdRangeFirstValue
                             FROM     dbo.Transactions
                             WHERE    IsCompleted = 1
                                      AND SurrogateIdRangeFirstValue BETWEEN @MinTransactionId AND @CurrentTransactionId
                                      AND SurrogateIdRangeFirstValue < @MinNotCompletedTransactionId
                             ORDER BY SurrogateIdRangeFirstValue DESC);
    IF @MaxTransactionId >= @MinTransactionId
        BEGIN
            UPDATE A
            SET    IsVisible   = 1,
                   VisibleDate = getUTCdate()
            FROM   dbo.Transactions AS A WITH (INDEX (1))
            WHERE  SurrogateIdRangeFirstValue BETWEEN @MinTransactionId AND @CurrentTransactionId
                   AND SurrogateIdRangeFirstValue <= @MaxTransactionId;
            SET @AffectedRows += @@rowcount;
        END
    SET @msg = 'Min=' + CONVERT (VARCHAR, @MinTransactionId) + ' C=' + CONVERT (VARCHAR, @CurrentTransactionId) + ' MinNC=' + CONVERT (VARCHAR, @MinNotCompletedTransactionId) + ' Max=' + CONVERT (VARCHAR, @MaxTransactionId);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @AffectedRows, @Text = @msg;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
