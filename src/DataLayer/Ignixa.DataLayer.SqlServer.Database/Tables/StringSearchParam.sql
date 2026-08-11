CREATE TABLE dbo.StringSearchParam (
    ResourceTypeId      SMALLINT       NOT NULL,
    ResourceSurrogateId BIGINT         NOT NULL,
    SearchParamId       SMALLINT       NOT NULL,
    Text                NVARCHAR (256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow        NVARCHAR (MAX) COLLATE Latin1_General_100_CI_AI_SC NULL,
    IsMin               BIT            CONSTRAINT string_IsMin_Constraint DEFAULT 0 NOT NULL,
    IsMax               BIT            CONSTRAINT string_IsMax_Constraint DEFAULT 0 NOT NULL
);

GO

ALTER TABLE dbo.StringSearchParam SET (LOCK_ESCALATION = AUTO);

GO

CREATE CLUSTERED INDEX IXC_StringSearchParam
    ON dbo.StringSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Text_INCLUDE_TextOverflow_IsMin_IsMax
    ON dbo.StringSearchParam(SearchParamId, Text)
    INCLUDE(TextOverflow, IsMin, IsMax) WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_Text_INCLUDE_IsMin_IsMax_WHERE_TextOverflow_NOT_NULL
    ON dbo.StringSearchParam(SearchParamId, Text)
    INCLUDE(IsMin, IsMax) WHERE TextOverflow IS NOT NULL WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);
