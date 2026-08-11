CREATE PROCEDURE dbo.UpsertSearchParamsWithOptimisticConcurrency
@searchParams dbo.SearchParamList READONLY
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (200) = NULL, @st AS DATETIME = getUTCdate();
BEGIN TRANSACTION;
DECLARE @lastUpdated AS DATETIMEOFFSET (7) = SYSDATETIMEOFFSET();
DECLARE @summaryOfChanges TABLE (
    Uri    VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Action VARCHAR (20)  NOT NULL);
DECLARE @conflictedRows TABLE (
    Uri VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL);
BEGIN TRY
    INSERT INTO @conflictedRows (Uri)
    SELECT sp.Uri
    FROM   @searchParams AS sp
           INNER JOIN
           dbo.SearchParam AS existing WITH (TABLOCKX)
           ON sp.Uri = existing.Uri
    WHERE  sp.LastUpdated != existing.LastUpdated;
    IF EXISTS (SELECT 1
               FROM   @conflictedRows)
        BEGIN
            DECLARE @conflictMessage AS NVARCHAR (4000);
            SELECT @conflictMessage = CONCAT('Optimistic concurrency conflict detected for search parameters: ', STRING_AGG(Uri, ', '))
            FROM   @conflictedRows;
            ROLLBACK;
            THROW 50001, @conflictMessage, 1;
        END
    MERGE INTO dbo.SearchParam
     AS target
    USING @searchParams AS source ON target.Uri = source.Uri
    WHEN MATCHED THEN UPDATE 
    SET Status               = source.Status,
        LastUpdated          = @lastUpdated,
        IsPartiallySupported = source.IsPartiallySupported
    WHEN NOT MATCHED BY TARGET THEN INSERT (Uri, Status, LastUpdated, IsPartiallySupported) VALUES (source.Uri, source.Status, @lastUpdated, source.IsPartiallySupported)
    OUTPUT source.Uri, $ACTION INTO @summaryOfChanges;
    SELECT SearchParamId,
           SearchParam.Uri,
           SearchParam.LastUpdated
    FROM   dbo.SearchParam AS searchParam
           INNER JOIN
           @summaryOfChanges AS upsertedSearchParam
           ON searchParam.Uri = upsertedSearchParam.Uri
    WHERE  upsertedSearchParam.Action = 'INSERT';
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH
