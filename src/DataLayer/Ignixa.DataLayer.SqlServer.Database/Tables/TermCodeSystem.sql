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

-- Matches this project's convention for high-volume shared tables (see the search-parameter tables, which
-- set the same option). On THOSE tables it is load-bearing: they are partitioned on
-- PartitionScheme_ResourceTypeId, so AUTO escalates a bulk operation's row locks to the partition it
-- touches rather than to the whole table. This table -- and the five other terminology tables that carry
-- this same setting -- is NOT partitioned, so AUTO here escalates to TABLE level exactly like SQL Server's
-- un-set default: it is currently inert. Kept anyway because it is harmless, matches the convention
-- LockEscalationConventionGuardTests enforces, and is already correct should this table ever be
-- partitioned. The concurrent-import table-lock concern this option was meant to address is therefore
-- still open here.
ALTER TABLE dbo.TermCodeSystem SET (LOCK_ESCALATION = AUTO);

GO

CREATE INDEX IX_TermCodeSystem_PackageResourceId
    ON dbo.TermCodeSystem(PackageResourceId);

GO

CREATE UNIQUE INDEX UQ_TermCodeSystem_System_Version
    ON dbo.TermCodeSystem(SystemId, Version)
    WHERE Version IS NOT NULL;
