CREATE TYPE dbo.NumberSearchParamList AS TABLE (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SingleValue         DECIMAL (36, 18) NULL,
    LowValue            DECIMAL (36, 18) NULL,
    HighValue           DECIMAL (36, 18) NULL UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue));
