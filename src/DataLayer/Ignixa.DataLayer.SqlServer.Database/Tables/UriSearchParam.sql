CREATE TABLE dbo.UriSearchParam (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    Uri                 VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Fragment             NVARCHAR (128) NULL,
    Version               NVARCHAR (64)  NULL
);

GO

ALTER TABLE dbo.UriSearchParam SET (LOCK_ESCALATION = AUTO);

GO

CREATE CLUSTERED INDEX IXC_UriSearchParam
    ON dbo.UriSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Uri
    ON dbo.UriSearchParam(SearchParamId, Uri) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);
