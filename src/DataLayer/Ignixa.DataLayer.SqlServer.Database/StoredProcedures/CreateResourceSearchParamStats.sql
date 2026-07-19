CREATE PROCEDURE dbo.CreateResourceSearchParamStats
@Table VARCHAR (100), @Column VARCHAR (100), @ResourceTypeId SMALLINT, @SearchParamId SMALLINT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = object_name(@@procid), @Mode AS VARCHAR (200) = 'T=' + isnull(@Table, 'NULL') + ' C=' + isnull(@Column, 'NULL') + ' RT=' + isnull(CONVERT (VARCHAR, @ResourceTypeId), 'NULL') + ' SP=' + isnull(CONVERT (VARCHAR, @SearchParamId), 'NULL'), @st AS DATETIME = getUTCdate();
BEGIN TRY
    IF @Table IS NULL
       OR @Column IS NULL
       OR @ResourceTypeId IS NULL
       OR @SearchParamId IS NULL
        RAISERROR ('@TableName IS NULL OR @KeyColumn IS NULL OR @ResourceTypeId IS NULL OR @SearchParamId IS NULL', 18, 127);
    EXECUTE ('CREATE STATISTICS ST_' + @Column + '_WHERE_ResourceTypeId_' + @ResourceTypeId + '_SearchParamId_' + @SearchParamId + ' ON dbo.' + @Table + ' (' + @Column + ') WHERE ResourceTypeId = ' + @ResourceTypeId + ' AND SearchParamId = ' + @SearchParamId);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Text = 'Stats created';
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    IF error_number() = 1927
        BEGIN
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
            RETURN;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH
