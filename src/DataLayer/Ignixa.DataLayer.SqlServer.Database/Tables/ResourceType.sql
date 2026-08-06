CREATE TABLE dbo.ResourceType (
    ResourceTypeId SMALLINT      IDENTITY (1, 1) NOT NULL,
    Name           NVARCHAR (50) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT UQ_ResourceType_ResourceTypeId UNIQUE (ResourceTypeId),
    CONSTRAINT PKC_ResourceType PRIMARY KEY CLUSTERED (Name) WITH (DATA_COMPRESSION = PAGE)
);
