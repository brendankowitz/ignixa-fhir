-- Embedding is NVARCHAR (MAX) here, not VECTOR (1536): the vector type is JSON-array-convertible
-- (CAST/implicit conversion to and from NVARCHAR/VARCHAR/JSON), which is what MergeVectorSearchParams.sql
-- relies on to CAST this column into dbo.VectorSearchParam.Embedding on insert. Table-valued parameters
-- are a pure transport shape; the real vector column lives only in dbo.VectorSearchParam.
CREATE TYPE dbo.VectorSearchParamList AS TABLE (
    ResourceTypeId       SMALLINT        NOT NULL,
    ResourceSurrogateId  BIGINT          NOT NULL,
    SearchParamId        SMALLINT        NOT NULL,
    EmbeddingModelId     SMALLINT        NOT NULL,
    ChunkOrdinal         SMALLINT        NOT NULL,
    SourceTextCompressed VARBINARY (MAX) NOT NULL,
    SourceTextHash       BINARY (32)     NOT NULL,
    Embedding            NVARCHAR (MAX)  NOT NULL PRIMARY KEY (ResourceTypeId, ResourceSurrogateId, SearchParamId, EmbeddingModelId, ChunkOrdinal));
