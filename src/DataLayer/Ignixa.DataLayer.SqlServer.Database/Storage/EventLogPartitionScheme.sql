CREATE PARTITION SCHEME EventLogPartitionScheme
    AS PARTITION EventLogPartitionFunction
    ALL TO ([PRIMARY]);
