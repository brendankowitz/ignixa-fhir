CREATE TABLE dbo.TermConceptMap (
    TermConceptMapId BIGINT         NOT NULL IDENTITY (1, 1),
    PackageResourceId BIGINT        NOT NULL,
    Canonical         NVARCHAR (512) NOT NULL,
    Version           NVARCHAR (100) NULL,
    Name              NVARCHAR (256) NOT NULL,
    SourceCanonical   NVARCHAR (512) NULL,
    TargetCanonical   NVARCHAR (512) NULL,
    ImportedDate      DATETIMEOFFSET DEFAULT getUTCdate() NOT NULL,
    CONSTRAINT PK_TermConceptMap PRIMARY KEY (TermConceptMapId),
    CONSTRAINT FK_TermConceptMap_PackageResource FOREIGN KEY (PackageResourceId) REFERENCES dbo.PackageResource (PackageResourceId) ON DELETE CASCADE
);

GO

-- Convention-only here, not load-bearing: this table is not partitioned, so AUTO escalates to TABLE
-- level exactly like SQL Server's un-set default. See TermCodeSystem.sql for the full explanation.
ALTER TABLE dbo.TermConceptMap SET (LOCK_ESCALATION = AUTO);

GO

CREATE INDEX IX_TermConceptMap_PackageResourceId
    ON dbo.TermConceptMap(PackageResourceId);

GO

CREATE UNIQUE INDEX UQ_TermConceptMap_Canonical_Version
    ON dbo.TermConceptMap(Canonical, Version)
    WHERE Version IS NOT NULL;
