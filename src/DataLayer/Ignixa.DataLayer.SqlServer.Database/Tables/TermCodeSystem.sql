CREATE TABLE dbo.TermCodeSystem (
    TermCodeSystemId BIGINT         NOT NULL IDENTITY (1, 1),
    PackageResourceId BIGINT        NOT NULL,
    SystemId          INT           NOT NULL,
    Version           NVARCHAR (100) NULL,
    ConceptCount      INT           NOT NULL,
    Content           NVARCHAR (50) NOT NULL,
    IsHierarchical    BIT           NOT NULL,
    CaseSensitive     BIT           NOT NULL,
    Compositional     BIT           NOT NULL,
    ImportedDate      DATETIMEOFFSET DEFAULT getUTCdate() NOT NULL,
    CONSTRAINT PK_TermCodeSystem PRIMARY KEY (TermCodeSystemId),
    CONSTRAINT FK_TermCodeSystem_PackageResource FOREIGN KEY (PackageResourceId) REFERENCES dbo.PackageResource (PackageResourceId) ON DELETE CASCADE,
    CONSTRAINT FK_TermCodeSystem_System FOREIGN KEY (SystemId) REFERENCES dbo.System (SystemId)
);

GO

CREATE INDEX IX_TermCodeSystem_PackageResourceId
    ON dbo.TermCodeSystem(PackageResourceId);

GO

CREATE UNIQUE INDEX UQ_TermCodeSystem_System_Version
    ON dbo.TermCodeSystem(SystemId, Version)
    WHERE Version IS NOT NULL;
