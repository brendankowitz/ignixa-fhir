CREATE TABLE dbo.TokenDateTimeCompositeSearchParam (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    SystemId1           INT           NULL,
    Code1               VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    StartDateTime2      DATETIME2 (7) NOT NULL,
    EndDateTime2        DATETIME2 (7) NOT NULL,
    IsLongerThanADay2   BIT           NOT NULL,
    CodeOverflow1       VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL
);

GO

ALTER TABLE dbo.TokenDateTimeCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

GO

CREATE CLUSTERED INDEX IXC_TokenDateTimeCompositeSearchParam
    ON dbo.TokenDateTimeCompositeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Code1_StartDateTime2_EndDateTime2_INCLUDE_SystemId1_IsLongerThanADay2
    ON dbo.TokenDateTimeCompositeSearchParam(SearchParamId, Code1, StartDateTime2, EndDateTime2)
    INCLUDE(SystemId1, IsLongerThanADay2) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_IsLongerThanADay2
    ON dbo.TokenDateTimeCompositeSearchParam(SearchParamId, Code1, EndDateTime2, StartDateTime2)
    INCLUDE(SystemId1, IsLongerThanADay2) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Code1_StartDateTime2_EndDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1
    ON dbo.TokenDateTimeCompositeSearchParam(SearchParamId, Code1, StartDateTime2, EndDateTime2)
    INCLUDE(SystemId1) WHERE IsLongerThanADay2 = 1 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1
    ON dbo.TokenDateTimeCompositeSearchParam(SearchParamId, Code1, EndDateTime2, StartDateTime2)
    INCLUDE(SystemId1) WHERE IsLongerThanADay2 = 1 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);
