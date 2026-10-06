CREATE TYPE dbo.VectorResourceList AS TABLE (
    ResourceTypeId      SMALLINT NOT NULL,
    ResourceSurrogateId BIGINT   NOT NULL PRIMARY KEY (ResourceTypeId, ResourceSurrogateId));
