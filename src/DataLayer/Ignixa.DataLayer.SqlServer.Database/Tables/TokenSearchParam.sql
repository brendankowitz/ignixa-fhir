CREATE TABLE dbo.TokenSearchParam (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    SystemId            INT           NULL,
    Code                VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow        VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL
);

GO

ALTER TABLE dbo.TokenSearchParam SET (LOCK_ESCALATION = AUTO);

GO

CREATE CLUSTERED INDEX IXC_TokenSearchParam
    ON dbo.TokenSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Code_INCLUDE_SystemId
    ON dbo.TokenSearchParam(SearchParamId, Code)
    INCLUDE(SystemId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);
