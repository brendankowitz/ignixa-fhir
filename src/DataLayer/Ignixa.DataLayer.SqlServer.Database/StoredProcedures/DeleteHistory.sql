CREATE PROCEDURE dbo.DeleteHistory
@DeleteResources BIT=0, @Reset BIT=0, @DisableLogEvent BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'DeleteHistory', @Mode AS VARCHAR (100) = 'D=' + isnull(CONVERT (VARCHAR, @DeleteResources), 'NULL') + ' R=' + isnull(CONVERT (VARCHAR, @Reset), 'NULL'), @st AS DATETIME = getUTCdate(), @Id AS VARCHAR (100) = 'DeleteHistory.LastProcessed.TypeId.SurrogateId', @ResourceTypeId AS SMALLINT, @SurrogateId AS BIGINT, @RowsToProcess AS INT, @ProcessedResources AS INT = 0, @DeletedResources AS INT = 0, @DeletedSearchParams AS INT = 0, @ReportDate AS DATETIME = getUTCdate();
BEGIN TRY
    IF @DisableLogEvent = 0
        INSERT INTO dbo.Parameters (Id, Char)
        SELECT @SP,
               'LogEvent';
    ELSE
        DELETE dbo.Parameters
        WHERE  Id = @SP;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    INSERT INTO dbo.Parameters (Id, Char)
    SELECT @Id,
           '0.0'
    WHERE  NOT EXISTS (SELECT *
                       FROM   dbo.Parameters
                       WHERE  Id = @Id);
    DECLARE @LastProcessed AS VARCHAR (100) = CASE WHEN @Reset = 0 THEN (SELECT Char
                                                                         FROM   dbo.Parameters
                                                                         WHERE  Id = @Id) ELSE '0.0' END;
    DECLARE @Types TABLE (
        ResourceTypeId SMALLINT      PRIMARY KEY,
        Name           VARCHAR (100));
    DECLARE @SurrogateIds TABLE (
        ResourceSurrogateId BIGINT PRIMARY KEY,
        IsHistory           BIT   );
    INSERT INTO @Types
    EXECUTE dbo.GetUsedResourceTypes ;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = '@Types', @Action = 'Insert', @Rows = @@rowcount;
    SET @ResourceTypeId = substring(@LastProcessed, 1, charindex('.', @LastProcessed) - 1);
    SET @SurrogateId = substring(@LastProcessed, charindex('.', @LastProcessed) + 1, 255);
    DELETE @Types
    WHERE  ResourceTypeId < @ResourceTypeId;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = '@Types', @Action = 'Delete', @Rows = @@rowcount;
    WHILE EXISTS (SELECT *
                  FROM   @Types)
        BEGIN
            SET @ResourceTypeId = (SELECT   TOP 1 ResourceTypeId
                                   FROM     @Types
                                   ORDER BY ResourceTypeId);
            SET @ProcessedResources = 0;
            SET @DeletedResources = 0;
            SET @DeletedSearchParams = 0;
            SET @RowsToProcess = 1;
            WHILE @RowsToProcess > 0
                BEGIN
                    DELETE @SurrogateIds;
                    INSERT INTO @SurrogateIds
                    SELECT   TOP 10000 ResourceSurrogateId,
                                       IsHistory
                    FROM     dbo.Resource
                    WHERE    ResourceTypeId = @ResourceTypeId
                             AND ResourceSurrogateId > @SurrogateId
                    ORDER BY ResourceSurrogateId;
                    SET @RowsToProcess = @@rowcount;
                    SET @ProcessedResources += @RowsToProcess;
                    IF @RowsToProcess > 0
                        SET @SurrogateId = (SELECT max(ResourceSurrogateId)
                                            FROM   @SurrogateIds);
                    SET @LastProcessed = CONVERT (VARCHAR, @ResourceTypeId) + '.' + CONVERT (VARCHAR, @SurrogateId);
                    DELETE @SurrogateIds
                    WHERE  IsHistory = 0;
                    IF EXISTS (SELECT *
                               FROM   @SurrogateIds)
                        BEGIN
                            DELETE dbo.ResourceWriteClaim
                            WHERE  ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                           FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.CompartmentAssignment
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.ReferenceSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.TokenSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.TokenText
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.StringSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.UriSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.NumberSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.QuantitySearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.DateTimeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.ReferenceTokenCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.TokenTokenCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.TokenDateTimeCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.TokenQuantityCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.TokenStringCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            DELETE dbo.TokenNumberNumberCompositeSearchParam
                            WHERE  ResourceTypeId = @ResourceTypeId
                                   AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                               FROM   @SurrogateIds);
                            SET @DeletedSearchParams += @@rowcount;
                            IF @DeleteResources = 1
                                BEGIN
                                    DELETE dbo.Resource
                                    WHERE  ResourceTypeId = @ResourceTypeId
                                           AND ResourceSurrogateId IN (SELECT ResourceSurrogateId
                                                                       FROM   @SurrogateIds);
                                    SET @DeletedResources += @@rowcount;
                                END
                        END
                    UPDATE dbo.Parameters
                    SET    Char = @LastProcessed
                    WHERE  Id = @Id;
                    IF datediff(second, @ReportDate, getUTCdate()) > 60
                        BEGIN
                            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = 'Resource', @Action = 'Select', @Rows = @ProcessedResources, @Text = @LastProcessed;
                            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = '*SearchParam', @Action = 'Delete', @Rows = @DeletedSearchParams, @Text = @LastProcessed;
                            IF @DeleteResources = 1
                                EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = 'Resource', @Action = 'Delete', @Rows = @DeletedResources, @Text = @LastProcessed;
                            SET @ReportDate = getUTCdate();
                            SET @ProcessedResources = 0;
                            SET @DeletedSearchParams = 0;
                            SET @DeletedResources = 0;
                        END
                END
            DELETE @Types
            WHERE  ResourceTypeId = @ResourceTypeId;
            SET @SurrogateId = 0;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = 'Resource', @Action = 'Select', @Rows = @ProcessedResources, @Text = @LastProcessed;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = '*SearchParam', @Action = 'Delete', @Rows = @DeletedSearchParams, @Text = @LastProcessed;
    IF @DeleteResources = 1
        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Run', @Target = 'Resource', @Action = 'Delete', @Rows = @DeletedResources, @Text = @LastProcessed;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
