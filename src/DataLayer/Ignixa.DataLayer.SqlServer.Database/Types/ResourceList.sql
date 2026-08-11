CREATE TYPE dbo.ResourceList AS TABLE (
    ResourceTypeId       SMALLINT        NOT NULL,
    ResourceSurrogateId  BIGINT          NOT NULL,
    ResourceId           VARCHAR (64)    COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version              INT             NOT NULL,
    HasVersionToCompare  BIT             NOT NULL,
    IsDeleted            BIT             NOT NULL,
    IsHistory            BIT             NOT NULL,
    KeepHistory          BIT             NOT NULL,
    RawResource          VARBINARY (MAX) NOT NULL,
    IsRawResourceMetaSet BIT             NOT NULL,
    RequestMethod        VARCHAR (10)    NULL,
    SearchParamHash      VARCHAR (64)    NULL PRIMARY KEY (ResourceTypeId, ResourceSurrogateId),
    UNIQUE (ResourceTypeId, ResourceId, Version));
