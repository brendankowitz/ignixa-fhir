CREATE TABLE dbo.DateTimeSearchParam (
    ResourceTypeId      SMALLINT      NOT NULL,
    ResourceSurrogateId BIGINT        NOT NULL,
    SearchParamId       SMALLINT      NOT NULL,
    StartDateTime       DATETIME2 (7) NOT NULL,
    EndDateTime         DATETIME2 (7) NOT NULL,
    IsLongerThanADay    BIT           NOT NULL,
    IsMin               BIT           CONSTRAINT date_IsMin_Constraint DEFAULT 0 NOT NULL,
    IsMax               BIT           CONSTRAINT date_IsMax_Constraint DEFAULT 0 NOT NULL
);

GO

ALTER TABLE dbo.DateTimeSearchParam SET (LOCK_ESCALATION = AUTO);

GO

CREATE CLUSTERED INDEX IXC_DateTimeSearchParam
    ON dbo.DateTimeSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax
    ON dbo.DateTimeSearchParam(SearchParamId, StartDateTime, EndDateTime)
    INCLUDE(IsLongerThanADay, IsMin, IsMax)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_EndDateTime_StartDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax
    ON dbo.DateTimeSearchParam(SearchParamId, EndDateTime, StartDateTime)
    INCLUDE(IsLongerThanADay, IsMin, IsMax)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1
    ON dbo.DateTimeSearchParam(SearchParamId, StartDateTime, EndDateTime)
    INCLUDE(IsMin, IsMax) WHERE IsLongerThanADay = 1
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

GO

CREATE INDEX IX_SearchParamId_EndDateTime_StartDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1
    ON dbo.DateTimeSearchParam(SearchParamId, EndDateTime, StartDateTime)
    INCLUDE(IsMin, IsMax) WHERE IsLongerThanADay = 1
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);
