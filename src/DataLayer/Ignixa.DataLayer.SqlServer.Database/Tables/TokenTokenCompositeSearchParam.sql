CREATE TABLE dbo.TokenTokenCompositeSearchParam (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    SystemId1           INT           NULL,
    Code1               VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2           INT           NULL,
    Code2               VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1       VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL,
    CodeOverflow2       VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL
);

GO

ALTER TABLE dbo.TokenTokenCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

GO

CREATE CLUSTERED INDEX IXC_TokenTokenCompositeSearchParam
    ON dbo.TokenTokenCompositeSearchParam(ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Code1_Code2_INCLUDE_SystemId1_SystemId2
    ON dbo.TokenTokenCompositeSearchParam(SearchParamId, Code1, Code2)
    INCLUDE(SystemId1, SystemId2) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);
