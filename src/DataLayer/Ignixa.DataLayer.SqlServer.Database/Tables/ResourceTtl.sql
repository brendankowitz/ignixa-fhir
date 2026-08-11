CREATE TABLE dbo.ResourceTtl (
    ResourceTypeId SMALLINT      NOT NULL,
    ResourceId     VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    ExpiresAt      DATETIMEOFFSET NOT NULL,
    TransactionId  BIGINT        NULL,
    CONSTRAINT PK_ResourceTtl PRIMARY KEY (ResourceTypeId, ResourceId)
);

GO

CREATE INDEX IX_ResourceTtl_ExpiresAt
    ON dbo.ResourceTtl(ExpiresAt);
