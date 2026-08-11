CREATE TABLE dbo.TokenNumberNumberCompositeSearchParam (
    ResourceTypeId      SMALLINT         NOT NULL,
    ResourceSurrogateId BIGINT           NOT NULL,
    SearchParamId       SMALLINT         NOT NULL,
    SystemId1           INT              NULL,
    Code1               VARCHAR (256)    COLLATE Latin1_General_100_CS_AS NOT NULL,
    SingleValue2        DECIMAL (36, 18) NULL,
    LowValue2           DECIMAL (36, 18) NULL,
    HighValue2          DECIMAL (36, 18) NULL,
    SingleValue3        DECIMAL (36, 18) NULL,
    LowValue3           DECIMAL (36, 18) NULL,
    HighValue3          DECIMAL (36, 18) NULL,
    HasRange            BIT              NOT NULL,
    CodeOverflow1       VARCHAR (MAX)    COLLATE Latin1_General_100_CS_AS NULL
);

GO

ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

GO

CREATE CLUSTERED INDEX IXC_TokenNumberNumberCompositeSearchParam
    ON dbo.TokenNumberNumberCompositeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Code1_SingleValue2_SingleValue3_INCLUDE_SystemId1_WHERE_HasRange_0
    ON dbo.TokenNumberNumberCompositeSearchParam(SearchParamId, Code1, SingleValue2, SingleValue3)
    INCLUDE(SystemId1) WHERE HasRange = 0 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3_INCLUDE_SystemId1_WHERE_HasRange_1
    ON dbo.TokenNumberNumberCompositeSearchParam(SearchParamId, Code1, LowValue2, HighValue2, LowValue3, HighValue3)
    INCLUDE(SystemId1) WHERE HasRange = 1 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);
