CREATE TYPE dbo.QuantitySearchParamList AS TABLE (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SystemId            INT              NULL,
    QuantityCodeId      INT              NULL,
    SingleValue         DECIMAL (36, 18) NULL,
    LowValue            DECIMAL (36, 18) NULL,
    HighValue           DECIMAL (36, 18) NULL UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue));
