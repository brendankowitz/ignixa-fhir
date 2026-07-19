CREATE TYPE dbo.ResourceKeyList AS TABLE (
    ResourceTypeId SMALLINT     NOT NULL,
    ResourceId     VARCHAR (64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version        INT          NULL UNIQUE (ResourceTypeId, ResourceId, Version));
