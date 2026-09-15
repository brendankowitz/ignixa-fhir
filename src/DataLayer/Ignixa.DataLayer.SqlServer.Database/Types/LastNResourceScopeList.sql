CREATE TYPE dbo.LastNResourceScopeList AS TABLE (
    ResourceTypeId SMALLINT NOT NULL,
    SearchParamId SMALLINT NOT NULL,
    ResourceSurrogateId BIGINT NOT NULL,
    PRIMARY KEY (ResourceTypeId, SearchParamId, ResourceSurrogateId)
);
