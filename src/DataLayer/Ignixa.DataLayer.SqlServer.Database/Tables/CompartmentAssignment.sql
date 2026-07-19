CREATE TABLE dbo.CompartmentAssignment (
    ResourceTypeId      SMALLINT     NOT NULL,
    ResourceSurrogateId BIGINT       NOT NULL,
    CompartmentTypeId   TINYINT      NOT NULL,
    ReferenceResourceId VARCHAR (64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory           BIT          NOT NULL,
    CONSTRAINT PKC_CompartmentAssignment PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
);

GO

ALTER TABLE dbo.CompartmentAssignment
    ADD CONSTRAINT DF_CompartmentAssignment_IsHistory DEFAULT 0 FOR IsHistory;

GO

ALTER TABLE dbo.CompartmentAssignment SET (LOCK_ESCALATION = AUTO);

GO

CREATE NONCLUSTERED INDEX IX_CompartmentAssignment_CompartmentTypeId_ReferenceResourceId
    ON dbo.CompartmentAssignment(ResourceTypeId, CompartmentTypeId, ReferenceResourceId, ResourceSurrogateId) WHERE IsHistory = 0 WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);
