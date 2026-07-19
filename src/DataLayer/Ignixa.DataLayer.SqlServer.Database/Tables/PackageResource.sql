CREATE TABLE dbo.PackageResource (
    PackageResourceId       BIGINT         NOT NULL IDENTITY (1, 1),
    PackageId                NVARCHAR (256) NOT NULL,
    PackageVersion           NVARCHAR (100) NOT NULL,
    ResourceType             NVARCHAR (64)  NOT NULL,
    Canonical                NVARCHAR (512) NOT NULL,
    Version                  NVARCHAR (100) NULL,
    ResourceId               NVARCHAR (64)  NOT NULL,
    ResourceJson             NVARCHAR (MAX) NOT NULL,
    FhirVersion              NVARCHAR (10)  NOT NULL,
    LoadedDate               DATETIMEOFFSET DEFAULT getUTCdate() NOT NULL,
    IsActive                 BIT            DEFAULT 1 NOT NULL,
    ContentHash              NVARCHAR (64)   NULL,
    ImportCompletedDate      DATETIMEOFFSET  NULL,
    ImportErrorMessage       NVARCHAR (1000) NULL,
    ImportStartDate          DATETIMEOFFSET  NULL,
    ImportedConceptCount     INT             NULL,
    TerminologyImportStatus  NVARCHAR (20)   NULL,
    CONSTRAINT PK_PackageResource PRIMARY KEY (PackageResourceId)
);

GO

CREATE UNIQUE INDEX UQ_PackageResource_Identity
    ON dbo.PackageResource(PackageId, PackageVersion, ResourceType, ResourceId);

GO

CREATE INDEX IX_PackageResource_Canonical_Version
    ON dbo.PackageResource(Canonical, Version)
    WHERE IsActive = 1;

GO

CREATE INDEX IX_PackageResource_ResourceType_Canonical
    ON dbo.PackageResource(ResourceType, Canonical)
    WHERE IsActive = 1;

GO

CREATE INDEX IX_PackageResource_Package
    ON dbo.PackageResource(PackageId, PackageVersion);

GO

CREATE INDEX IX_PackageResource_LoadedDate
    ON dbo.PackageResource(LoadedDate);
