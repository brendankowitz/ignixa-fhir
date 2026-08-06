CREATE TABLE dbo.TermValueSet (
    TermValueSetId         BIGINT         NOT NULL IDENTITY (1, 1),
    PackageResourceId      BIGINT         NOT NULL,
    Canonical              NVARCHAR (512) NOT NULL,
    Version                NVARCHAR (100) NULL,
    Name                   NVARCHAR (256) NOT NULL,
    Immutable              BIT            NOT NULL,
    IsExpanded             BIT            NOT NULL,
    LastExpansionDate      DATETIMEOFFSET NULL,
    ExpansionCodeCount     INT            NULL,
    IsPartialExpansion     BIT            DEFAULT 0 NOT NULL,
    PartialExpansionReason NVARCHAR (1024) NULL,
    ImportedDate           DATETIMEOFFSET DEFAULT getUTCdate() NOT NULL,
    CONSTRAINT PK_TermValueSet PRIMARY KEY (TermValueSetId),
    CONSTRAINT FK_TermValueSet_PackageResource FOREIGN KEY (PackageResourceId) REFERENCES dbo.PackageResource (PackageResourceId) ON DELETE CASCADE
);

GO

CREATE INDEX IX_TermValueSet_Canonical
    ON dbo.TermValueSet(Canonical)
    INCLUDE(Version, IsExpanded);

GO

CREATE INDEX IX_TermValueSet_PackageResourceId
    ON dbo.TermValueSet(PackageResourceId);

GO

CREATE UNIQUE INDEX UQ_TermValueSet_Canonical_Version
    ON dbo.TermValueSet(Canonical, Version)
    WHERE Version IS NOT NULL;
