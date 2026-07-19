CREATE PROCEDURE dbo.GetTransactions
@StartNotInclusiveTranId BIGINT, @EndInclusiveTranId BIGINT, @EndDate DATETIME=NULL
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (100) = 'ST=' + CONVERT (VARCHAR, @StartNotInclusiveTranId) + ' ET=' + CONVERT (VARCHAR, @EndInclusiveTranId) + ' ED=' + isnull(CONVERT (VARCHAR, @EndDate, 121), 'NULL'), @st AS DATETIME = getUTCdate();
IF @EndDate IS NULL
    SET @EndDate = getUTCdate();
SELECT   TOP 10000 SurrogateIdRangeFirstValue,
                   VisibleDate,
                   InvisibleHistoryRemovedDate
FROM     dbo.Transactions
WHERE    SurrogateIdRangeFirstValue > @StartNotInclusiveTranId
         AND SurrogateIdRangeFirstValue <= @EndInclusiveTranId
         AND EndDate <= @EndDate
ORDER BY SurrogateIdRangeFirstValue;
EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
