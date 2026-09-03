CREATE TABLE dbo.TermValueSetExpansion (
    TermValueSetExpansionId BIGINT         NOT NULL IDENTITY (1, 1),
    TermValueSetId          BIGINT         NOT NULL,
    SystemId                INT            NOT NULL,
    Code                    NVARCHAR (256) NOT NULL,
    Display                 NVARCHAR (500) NULL,
    SystemVersion           NVARCHAR (100) NULL,
    IsActive                BIT            NOT NULL,
    Ordinal                 INT            NOT NULL,
    CONSTRAINT PK_TermValueSetExpansion PRIMARY KEY (TermValueSetExpansionId),
    CONSTRAINT FK_TermValueSetExpansion_System FOREIGN KEY (SystemId) REFERENCES dbo.System (SystemId),
    CONSTRAINT FK_TermValueSetExpansion_ValueSet FOREIGN KEY (TermValueSetId) REFERENCES dbo.TermValueSet (TermValueSetId) ON DELETE CASCADE
);

GO

-- Convention-only here, not load-bearing: this table is not partitioned, so AUTO escalates to TABLE
-- level exactly like SQL Server's un-set default. See TermCodeSystem.sql for the full explanation.
ALTER TABLE dbo.TermValueSetExpansion SET (LOCK_ESCALATION = AUTO);

GO

CREATE INDEX IX_TermValueSetExpansion_Display
    ON dbo.TermValueSetExpansion(Display)
    INCLUDE(TermValueSetId, SystemId, Code)
    WHERE Display IS NOT NULL AND IsActive = 1;

GO

CREATE INDEX IX_TermValueSetExpansion_SystemId
    ON dbo.TermValueSetExpansion(SystemId);

GO

CREATE INDEX IX_TermValueSetExpansion_ValueSet_Ordinal
    ON dbo.TermValueSetExpansion(TermValueSetId, Ordinal)
    INCLUDE(SystemId, Code, Display)
    WHERE IsActive = 1;

GO

CREATE INDEX IX_TermValueSetExpansion_ValueSet_System_Code
    ON dbo.TermValueSetExpansion(TermValueSetId, SystemId, Code)
    WHERE IsActive = 1;
