CREATE TABLE dbo.ReferenceTokenCompositeSearchParam (
    ResourceTypeId            SMALLINT      NOT NULL,
    ResourceSurrogateId       BIGINT        NOT NULL,
    SearchParamId             SMALLINT      NOT NULL,
    BaseUri1                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId1  SMALLINT      NULL,
    ReferenceResourceId1      VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion1 INT           NULL,
    SystemId2                 INT           NULL,
    Code2                     VARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow2             VARCHAR (MAX) COLLATE Latin1_General_100_CS_AS NULL
);

GO

ALTER TABLE dbo.ReferenceTokenCompositeSearchParam SET (LOCK_ESCALATION = AUTO);

GO

CREATE CLUSTERED INDEX IXC_ReferenceTokenCompositeSearchParam
    ON dbo.ReferenceTokenCompositeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_ReferenceResourceId1_Code2_INCLUDE_ReferenceResourceTypeId1_BaseUri1_SystemId2
    ON dbo.ReferenceTokenCompositeSearchParam(SearchParamId, ReferenceResourceId1, Code2)
    INCLUDE(ReferenceResourceTypeId1, BaseUri1, SystemId2) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);
