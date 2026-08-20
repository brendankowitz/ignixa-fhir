CREATE PROCEDURE dbo.MergeResourcesGetTransactionVisibility
@TransactionId BIGINT OUTPUT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (100) = '', @st AS DATETIME = getUTCdate();
SET @TransactionId = isnull((SELECT   TOP 1 SurrogateIdRangeFirstValue
                             FROM     dbo.Transactions
                             WHERE    IsVisible = 1
                             ORDER BY SurrogateIdRangeFirstValue DESC), -1);
EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount, @Text = @TransactionId;
