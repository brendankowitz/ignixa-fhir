CREATE TABLE dbo.NumberSearchParam (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SingleValue         DECIMAL (36, 18) NULL,
    LowValue            DECIMAL (36, 18) NOT NULL,
    HighValue           DECIMAL (36, 18) NOT NULL
);

GO

ALTER TABLE dbo.NumberSearchParam SET (LOCK_ESCALATION = AUTO);

GO

CREATE CLUSTERED INDEX IXC_NumberSearchParam
    ON dbo.NumberSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_SingleValue_WHERE_SingleValue_NOT_NULL
    ON dbo.NumberSearchParam(SearchParamId, SingleValue) WHERE SingleValue IS NOT NULL
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_LowValue_HighValue
    ON dbo.NumberSearchParam(SearchParamId, LowValue, HighValue)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_HighValue_LowValue
    ON dbo.NumberSearchParam(SearchParamId, HighValue, LowValue)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);
