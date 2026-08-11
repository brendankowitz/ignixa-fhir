CREATE PROCEDURE dbo.DisableIndexes
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'DisableIndexes', @Mode AS VARCHAR (200) = '', @st AS DATETIME = getUTCdate(), @Tbl AS VARCHAR (100), @Ind AS VARCHAR (200), @Txt AS VARCHAR (4000);
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    DECLARE @Tables TABLE (
        Tbl       VARCHAR (100) PRIMARY KEY,
        Supported BIT          );
    INSERT INTO @Tables
    EXECUTE dbo.GetPartitionedTables @IncludeNotDisabled = 1, @IncludeNotSupported = 0;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Tables', @Action = 'Insert', @Rows = @@rowcount;
    DECLARE @Indexes TABLE (
        Tbl   VARCHAR (100),
        Ind   VARCHAR (200),
        TblId INT          ,
        IndId INT           PRIMARY KEY (Tbl, Ind));
    INSERT INTO @Indexes
    SELECT Tbl,
           I.Name,
           TblId,
           I.index_id
    FROM   (SELECT object_id(Tbl) AS TblId,
                   Tbl
            FROM   @Tables) AS O
           INNER JOIN
           sys.indexes AS I
           ON I.object_id = TblId;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Indexes', @Action = 'Insert', @Rows = @@rowcount;
    INSERT INTO dbo.IndexProperties (TableName, IndexName, PropertyName, PropertyValue)
    SELECT Tbl,
           Ind,
           'DATA_COMPRESSION',
           data_comp
    FROM   (SELECT Tbl,
                   Ind,
                   isnull((SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END
                           FROM   sys.partitions
                           WHERE  object_id = TblId
                                  AND index_id = IndId), 'NONE') AS data_comp
            FROM   @Indexes) AS A
    WHERE  NOT EXISTS (SELECT *
                       FROM   dbo.IndexProperties
                       WHERE  TableName = Tbl
                              AND IndexName = Ind);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = 'IndexProperties', @Action = 'Insert', @Rows = @@rowcount;
    DELETE @Indexes
    WHERE  Tbl = 'Resource'
           OR IndId = 1;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Indexes', @Action = 'Delete', @Rows = @@rowcount;
    WHILE EXISTS (SELECT *
                  FROM   @Indexes)
        BEGIN
            SELECT TOP 1 @Tbl = Tbl,
                         @Ind = Ind
            FROM   @Indexes;
            SET @Txt = 'ALTER INDEX ' + @Ind + ' ON dbo.' + @Tbl + ' DISABLE';
            EXECUTE (@Txt);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @Ind, @Action = 'Disable', @Text = @Txt;
            DELETE @Indexes
            WHERE  Tbl = @Tbl
                   AND Ind = @Ind;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH
