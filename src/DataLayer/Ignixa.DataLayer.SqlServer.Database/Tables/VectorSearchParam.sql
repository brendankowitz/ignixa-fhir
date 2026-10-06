-- Not partitioned on ResourceTypeId, unlike every sibling *SearchParam table (see StringSearchParam.sql),
-- and not DATA_COMPRESSION'd either. Both are deliberate, not oversights:
--
-- DATA_COMPRESSION = ROW/PAGE is rejected outright by the SQL Database Engine for any table that has a
-- vector column (confirmed against the local SQL Server 2025 engine this schema targets). There is no
-- partial-compression escape hatch for just the non-vector columns of the same table, so every column here
-- goes uncompressed together.
--
-- Partitioning by ResourceTypeId, unlike compression, is not rejected by the engine -- a partitioned
-- table with a vector column deploys cleanly. It is left out anyway to match the upstream fhir-server
-- design this schema ports (microsoft/fhir-server#5802), which keeps VectorSearchParam on a single
-- partition for a future DiskANN approximate-nearest-neighbor index. DiskANN vector indexes are built per
-- table, not per partition, so splitting the rows sixteen ways by ResourceTypeId today would require
-- rebuilding every partition's content into one table before the engine's vector index tooling could ever
-- be added, rather than adding an index to what is already here. If exact (non-ANN) search ever proves to
-- be the permanent query strategy, partitioning can be reconsidered independently.
CREATE TABLE dbo.VectorSearchParam (
    ResourceTypeId       SMALLINT        NOT NULL,
    ResourceSurrogateId  BIGINT          NOT NULL,
    SearchParamId        SMALLINT        NOT NULL,
    EmbeddingModelId     SMALLINT        NOT NULL,
    ChunkOrdinal         SMALLINT        NOT NULL,
    SourceTextCompressed VARBINARY (MAX) NOT NULL,
    SourceTextHash       BINARY (32)     NOT NULL,
    Embedding            VECTOR (1536)   NOT NULL,
    CONSTRAINT PKC_VectorSearchParam PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId, SearchParamId, EmbeddingModelId, ChunkOrdinal)
);

GO

ALTER TABLE dbo.VectorSearchParam SET (LOCK_ESCALATION = AUTO);
