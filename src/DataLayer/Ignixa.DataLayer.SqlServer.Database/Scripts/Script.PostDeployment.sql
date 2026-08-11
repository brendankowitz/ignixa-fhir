IF (SELECT COUNT(*) FROM sys.partition_range_values prv
    JOIN sys.partition_functions pf ON pf.function_id = prv.function_id
    WHERE pf.name = 'PartitionFunction_ResourceChangeData_Timestamp') <= 1
BEGIN
    DECLARE @numberOfHistoryPartitions AS INT = 48;
    DECLARE @numberOfFuturePartitions AS INT = 720;
    DECLARE @rightPartitionBoundary AS DATETIME2 (7);
    DECLARE @currentDateTime AS DATETIME2 (7) = sysutcdatetime();
    WHILE @numberOfHistoryPartitions >= -@numberOfFuturePartitions
        BEGIN
            SET @rightPartitionBoundary = DATEADD(hour, DATEDIFF(hour, 0, @currentDateTime) - @numberOfHistoryPartitions, 0);
            ALTER PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp NEXT USED [Primary];
            ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp( )
                SPLIT RANGE (@rightPartitionBoundary);
            SET @numberOfHistoryPartitions -= 1;
        END
END

IF NOT EXISTS (SELECT 1 FROM dbo.ResourceChangeType WHERE ResourceChangeTypeId = 0)
    INSERT dbo.ResourceChangeType (ResourceChangeTypeId, Name) VALUES (0, N'Creation');

IF NOT EXISTS (SELECT 1 FROM dbo.ResourceChangeType WHERE ResourceChangeTypeId = 1)
    INSERT dbo.ResourceChangeType (ResourceChangeTypeId, Name) VALUES (1, N'Update');

IF NOT EXISTS (SELECT 1 FROM dbo.ResourceChangeType WHERE ResourceChangeTypeId = 2)
    INSERT dbo.ResourceChangeType (ResourceChangeTypeId, Name) VALUES (2, N'Deletion');
