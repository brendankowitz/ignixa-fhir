CREATE TABLE dbo.TokenQuantityCompositeSearchParam (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SystemId1           INT              NULL,
    Code1               VARCHAR (256)    COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2           INT              NULL,
    QuantityCodeId2     INT              NULL,
    SingleValue2        DECIMAL (36, 18) NULL,
    LowValue2           DECIMAL (36, 18) NULL,
    HighValue2          DECIMAL (36, 18) NULL,
    CodeOverflow1       VARCHAR (MAX)    COLLATE Latin1_General_100_CS_AS NULL
);

GO

ALTER TABLE dbo.TokenQuantityCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

GO

CREATE CLUSTERED INDEX IXC_TokenQuantityCompositeSearchParam
    ON dbo.TokenQuantityCompositeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Code1_SingleValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_SingleValue2_NOT_NULL
    ON dbo.TokenQuantityCompositeSearchParam(SearchParamId, Code1, SingleValue2)
    INCLUDE(QuantityCodeId2, SystemId1, SystemId2) WHERE SingleValue2 IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Code1_LowValue2_HighValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_LowValue2_NOT_NULL
    ON dbo.TokenQuantityCompositeSearchParam(SearchParamId, Code1, LowValue2, HighValue2)
    INCLUDE(QuantityCodeId2, SystemId1, SystemId2) WHERE LowValue2 IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Code1_HighValue2_LowValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_LowValue2_NOT_NULL
    ON dbo.TokenQuantityCompositeSearchParam(SearchParamId, Code1, HighValue2, LowValue2)
    INCLUDE(QuantityCodeId2, SystemId1, SystemId2) WHERE LowValue2 IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);
