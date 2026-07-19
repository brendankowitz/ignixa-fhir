CREATE PROCEDURE dbo.GetUsedResourceTypes
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetUsedResourceTypes', @Mode AS VARCHAR (100) = '', @st AS DATETIME = getUTCdate();
BEGIN TRY
    SELECT ResourceTypeId,
           Name
    FROM   dbo.ResourceType AS A
    WHERE  EXISTS (SELECT *
                   FROM   dbo.Resource AS B
                   WHERE  B.ResourceTypeId = A.ResourceTypeId);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
