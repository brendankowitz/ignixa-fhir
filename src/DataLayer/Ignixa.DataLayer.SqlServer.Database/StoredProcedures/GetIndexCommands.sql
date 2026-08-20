CREATE PROCEDURE dbo.GetIndexCommands
@Tbl VARCHAR (100), @Ind VARCHAR (200), @AddPartClause BIT, @IncludeClustered BIT, @Txt VARCHAR (MAX)=NULL OUTPUT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetIndexCommands', @Mode AS VARCHAR (200) = 'Tbl=' + isnull(@Tbl, 'NULL') + ' Ind=' + isnull(@Ind, 'NULL'), @st AS DATETIME = getUTCdate();
DECLARE @Indexes TABLE (
    Ind VARCHAR (200) PRIMARY KEY,
    Txt VARCHAR (MAX));
BEGIN TRY
    IF @Tbl IS NULL
        RAISERROR ('@Tbl IS NULL', 18, 127);
    INSERT INTO @Indexes
    SELECT Ind,
           CASE WHEN is_primary_key = 1 THEN 'ALTER TABLE dbo.[' + Tbl + '] ADD PRIMARY KEY ' + CASE WHEN type = 1 THEN ' CLUSTERED' ELSE '' END ELSE 'CREATE' + CASE WHEN is_unique = 1 THEN ' UNIQUE' ELSE '' END + CASE WHEN type = 1 THEN ' CLUSTERED' ELSE '' END + ' INDEX ' + Ind + ' ON dbo.[' + Tbl + ']' END + ' (' + KeyCols + ')' + IncClause + CASE WHEN filter_def IS NOT NULL THEN ' WHERE ' + filter_def ELSE '' END + CASE WHEN data_comp IS NOT NULL THEN ' WITH (DATA_COMPRESSION = ' + data_comp + ')' ELSE '' END + CASE WHEN @AddPartClause = 1 THEN PartClause ELSE '' END
    FROM   (SELECT O.Name AS Tbl,
                   I.Name AS Ind,
                   isnull((SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END
                           FROM   sys.partitions AS P
                           WHERE  P.object_id = I.object_id
                                  AND I.index_id = P.index_id), (SELECT NULLIF (PropertyValue, 'NONE')
                                                                 FROM   dbo.IndexProperties
                                                                 WHERE  TableName = O.Name
                                                                        AND IndexName = I.Name
                                                                        AND PropertyName = 'DATA_COMPRESSION')) AS data_comp,
                   replace(replace(replace(replace(I.filter_definition, '[', ''), ']', ''), '(', ''), ')', '') AS filter_def,
                   I.is_unique,
                   I.is_primary_key,
                   I.type,
                   KeyCols,
                   CASE WHEN IncCols IS NOT NULL THEN ' INCLUDE (' + IncCols + ')' ELSE '' END AS IncClause,
                   CASE WHEN EXISTS (SELECT *
                                     FROM   sys.partition_schemes AS S
                                     WHERE  S.data_space_id = I.data_space_id
                                            AND name = 'PartitionScheme_ResourceTypeId') THEN ' ON PartitionScheme_ResourceTypeId (ResourceTypeId)' ELSE '' END AS PartClause
            FROM   sys.indexes AS I
                   INNER JOIN
                   sys.objects AS O
                   ON O.object_id = I.object_id CROSS APPLY (SELECT   string_agg(CASE WHEN IC.key_ordinal > 0
                                                                                           AND IC.is_included_column = 0 THEN C.name END, ',') WITHIN GROUP (ORDER BY key_ordinal) AS KeyCols,
                                                                      string_agg(CASE WHEN IC.is_included_column = 1 THEN C.name END, ',') WITHIN GROUP (ORDER BY key_ordinal) AS IncCols
                                                             FROM     sys.index_columns AS IC
                                                                      INNER JOIN
                                                                      sys.columns AS C
                                                                      ON C.object_id = IC.object_id
                                                                         AND C.column_id = IC.column_id
                                                             WHERE    IC.object_id = I.object_id
                                                                      AND IC.index_id = I.index_id
                                                             GROUP BY IC.object_id, IC.index_id) AS IC
            WHERE  O.name = @Tbl
                   AND (@Ind IS NULL
                        OR I.name = @Ind)
                   AND (@IncludeClustered = 1
                        OR index_id > 1)) AS A;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Indexes', @Action = 'Insert', @Rows = @@rowcount;
    IF @Ind IS NULL
        SELECT Ind,
               Txt
        FROM   @Indexes;
    ELSE
        SET @Txt = (SELECT Txt
                    FROM   @Indexes);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Text = @Txt;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH
