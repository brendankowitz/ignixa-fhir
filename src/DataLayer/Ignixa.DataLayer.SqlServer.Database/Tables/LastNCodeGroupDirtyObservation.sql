CREATE TABLE dbo.LastNCodeGroupDirtyObservation (
    ResourceTypeId SMALLINT NOT NULL,
    SearchParamId SMALLINT NOT NULL,
    Generation BIGINT NOT NULL,
    ResourceSurrogateId BIGINT NOT NULL,
    CONSTRAINT PK_LastNCodeGroupDirtyObservation PRIMARY KEY CLUSTERED
        (ResourceTypeId, SearchParamId, Generation, ResourceSurrogateId)
);
