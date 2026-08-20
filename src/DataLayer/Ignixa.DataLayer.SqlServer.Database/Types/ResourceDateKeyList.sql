CREATE TYPE dbo.ResourceDateKeyList AS TABLE (
    ResourceTypeId      SMALLINT     NOT NULL,
    ResourceId          VARCHAR (64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ResourceSurrogateId BIGINT       NOT NULL PRIMARY KEY (ResourceTypeId, ResourceId, ResourceSurrogateId));
