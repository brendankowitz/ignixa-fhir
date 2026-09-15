CREATE TABLE dbo.LastNObservationCodeGroup (
    ResourceTypeId SMALLINT NOT NULL,
    SearchParamId SMALLINT NOT NULL,
    ResourceSurrogateId BIGINT NOT NULL,
    GroupKind TINYINT NOT NULL,
    CodeGroupId BIGINT NULL,
    TextCode NVARCHAR(400) COLLATE Latin1_General_100_CS_AS NULL,
    CONSTRAINT PK_LastNObservationCodeGroup PRIMARY KEY CLUSTERED
        (ResourceTypeId, SearchParamId, ResourceSurrogateId),
    CONSTRAINT CH_LastNObservationCodeGroup_Representation CHECK (
        (GroupKind = 0 AND CodeGroupId IS NOT NULL AND TextCode IS NULL)
        OR (GroupKind = 1 AND CodeGroupId IS NULL AND TextCode IS NOT NULL))
);

GO

CREATE INDEX IX_LastNObservationCodeGroup_Rank
    ON dbo.LastNObservationCodeGroup
        (ResourceTypeId, SearchParamId, GroupKind, CodeGroupId, TextCode, ResourceSurrogateId);
