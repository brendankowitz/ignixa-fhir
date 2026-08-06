CREATE TABLE dbo.TokenStringCompositeSearchParam (
    ResourceTypeId      SMALLINT       NOT NULL,
    ResourceSurrogateId BIGINT         NOT NULL,
    SearchParamId       SMALLINT       NOT NULL,
    SystemId1           INT            NULL,
    Code1               VARCHAR (256)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    Text2               NVARCHAR (256) COLLATE Latin1_General_CI_AI NOT NULL,
    TextOverflow2       NVARCHAR (MAX) COLLATE Latin1_General_CI_AI NULL,
    CodeOverflow1       VARCHAR (MAX)  COLLATE Latin1_General_100_CS_AS NULL
);

GO

ALTER TABLE dbo.TokenStringCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

GO

CREATE CLUSTERED INDEX IXC_TokenStringCompositeSearchParam
    ON dbo.TokenStringCompositeSearchParam(ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Code1_Text2_INCLUDE_SystemId1_TextOverflow2
    ON dbo.TokenStringCompositeSearchParam(SearchParamId, Code1, Text2)
    INCLUDE(SystemId1, TextOverflow2) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Code1_Text2_INCLUDE_SystemId1_WHERE_TextOverflow2_NOT_NULL
    ON dbo.TokenStringCompositeSearchParam(SearchParamId, Code1, Text2)
    INCLUDE(SystemId1) WHERE TextOverflow2 IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);
