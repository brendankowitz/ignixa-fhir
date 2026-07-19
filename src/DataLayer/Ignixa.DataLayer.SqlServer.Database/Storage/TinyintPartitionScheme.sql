CREATE PARTITION SCHEME TinyintPartitionScheme
    AS PARTITION TinyintPartitionFunction
    ALL TO ([PRIMARY]);
