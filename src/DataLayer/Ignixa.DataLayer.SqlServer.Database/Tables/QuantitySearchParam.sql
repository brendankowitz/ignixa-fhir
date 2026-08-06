CREATE TABLE dbo.QuantitySearchParam (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SystemId            INT              NULL,
    QuantityCodeId      INT              NULL,
    SingleValue         DECIMAL (36, 18) NULL,
    LowValue            DECIMAL (36, 18) NOT NULL,
    HighValue           DECIMAL (36, 18) NOT NULL
);

GO

ALTER TABLE dbo.QuantitySearchParam SET (LOCK_ESCALATION = AUTO);

GO

CREATE CLUSTERED INDEX IXC_QuantitySearchParam
    ON dbo.QuantitySearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_QuantityCodeId_SingleValue_INCLUDE_SystemId_WHERE_SingleValue_NOT_NULL
    ON dbo.QuantitySearchParam(SearchParamId, QuantityCodeId, SingleValue)
    INCLUDE(SystemId) WHERE SingleValue IS NOT NULL
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_QuantityCodeId_LowValue_HighValue_INCLUDE_SystemId
    ON dbo.QuantitySearchParam(SearchParamId, QuantityCodeId, LowValue, HighValue)
    INCLUDE(SystemId)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_QuantityCodeId_HighValue_LowValue_INCLUDE_SystemId
    ON dbo.QuantitySearchParam(SearchParamId, QuantityCodeId, HighValue, LowValue)
    INCLUDE(SystemId)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);
