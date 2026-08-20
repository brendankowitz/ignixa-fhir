/*
    Hourly partition maintenance for ResourceChangeData.

    PartitionFunction_ResourceChangeData_Timestamp is created with exactly ONE declared
    boundary (see Storage/PartitionFunction_ResourceChangeData_Timestamp.sql). The loop
    below adds the remaining 769 -- 48 hourly boundaries of history, the current hour, and
    720 of future -- so a fully deployed database has 770 boundaries in total. That total
    is therefore a function of this loop's bounds PLUS the boundary declared by the
    partition function itself; changing either changes the expected count.

    WHY THE GUARD IS PER-BOUNDARY AND NOT ALL-OR-NOTHING:

    Each iteration issues its own ALTER PARTITION SCHEME / ALTER PARTITION FUNCTION pair.
    SPLIT RANGE takes a single boundary, so the work cannot be batched, and each statement
    auto-commits on its own -- there is no enclosing transaction to roll back. An
    interrupted publish (command timeout, dropped connection, cancelled deployment) leaves
    the function partially split, e.g. at 40 of 770 boundaries.

    Guarding the whole block on the total boundary count ("only run while the function is
    still at its initial single boundary") treats ANY partial progress as completion. A run
    interrupted at 40 boundaries would be skipped by every subsequent publish, leaving the
    database permanently under-partitioned while each deployment reported success -- a
    silent, unrecoverable degradation. Testing each boundary individually instead makes the
    loop resumable: a partial run finishes on the next publish, and an already-complete
    database performs zero splits.

    TYPE COMPARISON:

    sys.partition_range_values.value is SQL_VARIANT. Both sides are converted explicitly to
    DATETIME2(7) -- the partition function's declared parameter type -- so the equality test
    compares like with like rather than relying on SQL_VARIANT comparison semantics. This
    match is load-bearing, not cosmetic: if it failed to recognise an existing boundary the
    loop would attempt to re-split it and the publish would fail outright.

    ANCHOR:

    @currentDateTime is sampled once per run, so every boundary in a single run shares one
    anchor. Re-publishing within the same UTC hour computes an identical boundary set and
    performs no splits. A publish in a later hour extends the rolling window forward by the
    hours elapsed, which is the intended behaviour for a time-partitioned change feed.
*/
DECLARE @numberOfHistoryPartitions AS INT = 48;
DECLARE @numberOfFuturePartitions AS INT = 720;
DECLARE @rightPartitionBoundary AS DATETIME2 (7);
DECLARE @currentDateTime AS DATETIME2 (7) = sysutcdatetime();

WHILE @numberOfHistoryPartitions >= -@numberOfFuturePartitions
    BEGIN
        SET @rightPartitionBoundary = DATEADD(hour, DATEDIFF(hour, 0, @currentDateTime) - @numberOfHistoryPartitions, 0);

        IF NOT EXISTS (SELECT 1
                       FROM   sys.partition_range_values AS prv
                              JOIN sys.partition_functions AS pf
                                ON pf.function_id = prv.function_id
                       WHERE  pf.name = 'PartitionFunction_ResourceChangeData_Timestamp'
                              AND CONVERT (DATETIME2 (7), prv.value) = @rightPartitionBoundary)
            BEGIN
                ALTER PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp NEXT USED [PRIMARY];
                ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp( )
                    SPLIT RANGE (@rightPartitionBoundary);
            END

        SET @numberOfHistoryPartitions -= 1;
    END

/*
    Seed rows are guarded per row for the same reason the partition split is: a single
    guard covering all three (for example "if the table is empty") would treat a run
    interrupted after the first INSERT as complete, and the missing rows would never be
    added. Testing each key individually makes a partially seeded table self-heal on the
    next publish, and a fully seeded table a no-op.
*/
IF NOT EXISTS (SELECT 1 FROM dbo.ResourceChangeType WHERE ResourceChangeTypeId = 0)
    INSERT dbo.ResourceChangeType (ResourceChangeTypeId, Name) VALUES (0, N'Creation');

IF NOT EXISTS (SELECT 1 FROM dbo.ResourceChangeType WHERE ResourceChangeTypeId = 1)
    INSERT dbo.ResourceChangeType (ResourceChangeTypeId, Name) VALUES (1, N'Update');

IF NOT EXISTS (SELECT 1 FROM dbo.ResourceChangeType WHERE ResourceChangeTypeId = 2)
    INSERT dbo.ResourceChangeType (ResourceChangeTypeId, Name) VALUES (2, N'Deletion');
