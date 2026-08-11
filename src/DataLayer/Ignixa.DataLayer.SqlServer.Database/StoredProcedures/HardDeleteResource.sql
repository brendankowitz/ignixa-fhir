CREATE PROCEDURE dbo.HardDeleteResource
@ResourceTypeId SMALLINT, @ResourceId VARCHAR (64), @KeepCurrentVersion BIT, @IsResourceChangeCaptureEnabled BIT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (200) = 'RT=' + CONVERT (VARCHAR, @ResourceTypeId) + ' R=' + @ResourceId + ' V=' + CONVERT (VARCHAR, @KeepCurrentVersion) + ' CC=' + CONVERT (VARCHAR, @IsResourceChangeCaptureEnabled), @st AS DATETIME = getUTCdate(), @TransactionId AS BIGINT;
BEGIN TRY
    IF @IsResourceChangeCaptureEnabled = 1
        EXECUTE dbo.MergeResourcesBeginTransaction @Count = 1, @TransactionId = @TransactionId OUTPUT;
    IF @KeepCurrentVersion = 0
        BEGIN TRANSACTION;
    DECLARE @SurrogateIds TABLE (
        ResourceSurrogateId BIGINT NOT NULL);
    IF @IsResourceChangeCaptureEnabled = 1
       AND NOT EXISTS (SELECT *
                       FROM   dbo.Parameters
                       WHERE  Id = 'InvisibleHistory.IsEnabled'
                              AND Number = 0)
        UPDATE dbo.Resource
        SET    IsDeleted            = 1,
               RawResource          = 0xF,
               SearchParamHash      = NULL,
               HistoryTransactionId = @TransactionId
        OUTPUT deleted.ResourceSurrogateId INTO @SurrogateIds
        WHERE  ResourceTypeId = @ResourceTypeId
               AND ResourceId = @ResourceId
               AND (@KeepCurrentVersion = 0
                    OR IsHistory = 1)
               AND RawResource <> 0xF;
    ELSE
        DELETE dbo.Resource
        OUTPUT deleted.ResourceSurrogateId INTO @SurrogateIds
        WHERE  ResourceTypeId = @ResourceTypeId
               AND ResourceId = @ResourceId
               AND (@KeepCurrentVersion = 0
                    OR IsHistory = 1)
               AND RawResource <> 0xF;
    IF @KeepCurrentVersion = 0
        BEGIN
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.ResourceWriteClaim AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.ReferenceSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.TokenSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.TokenText AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.StringSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.UriSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.NumberSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.QuantitySearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.DateTimeSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.ReferenceTokenCompositeSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.TokenTokenCompositeSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.TokenDateTimeCompositeSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.TokenQuantityCompositeSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.TokenStringCompositeSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
            DELETE B
            FROM   @SurrogateIds AS A
                   INNER LOOP JOIN
                   dbo.TokenNumberNumberCompositeSearchParam AS B WITH (INDEX (1), FORCESEEK, PAGLOCK)
                   ON B.ResourceTypeId = @ResourceTypeId
                      AND B.ResourceSurrogateId = A.ResourceSurrogateId
            OPTION (MAXDOP 1);
        END
    IF @@trancount > 0
        COMMIT TRANSACTION;
    IF @IsResourceChangeCaptureEnabled = 1
        EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
END TRY
BEGIN CATCH
    IF @@trancount > 0
        ROLLBACK;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH
