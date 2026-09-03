CREATE TABLE dbo.TermConcept (
    TermConceptId    BIGINT         NOT NULL IDENTITY (1, 1),
    TermCodeSystemId BIGINT         NOT NULL,
    Code             NVARCHAR (256) NOT NULL,
    Display          NVARCHAR (500) NULL,
    Definition       NVARCHAR (4000) NULL,
    ParentConceptId  BIGINT         NULL,
    Level            INT            NOT NULL,
    IsActive         BIT            NOT NULL,
    PropertiesJson   NVARCHAR (MAX) NULL,
    CONSTRAINT PK_TermConcept PRIMARY KEY (TermConceptId),
    CONSTRAINT FK_TermConcept_CodeSystem FOREIGN KEY (TermCodeSystemId) REFERENCES dbo.TermCodeSystem (TermCodeSystemId) ON DELETE CASCADE,
    CONSTRAINT FK_TermConcept_Parent FOREIGN KEY (ParentConceptId) REFERENCES dbo.TermConcept (TermConceptId)
);

GO

-- Convention-only here, not load-bearing: this table is not partitioned, so AUTO escalates to TABLE
-- level exactly like SQL Server's un-set default. See TermCodeSystem.sql for the full explanation.
ALTER TABLE dbo.TermConcept SET (LOCK_ESCALATION = AUTO);

GO

CREATE INDEX IX_TermConcept_CodeSystem_Code_Active
    ON dbo.TermConcept(TermCodeSystemId, Code, IsActive)
    INCLUDE(Display, Definition);

GO

CREATE INDEX IX_TermConcept_Display
    ON dbo.TermConcept(Display)
    INCLUDE(TermCodeSystemId, Code)
    WHERE Display IS NOT NULL;

GO

CREATE INDEX IX_TermConcept_Parent
    ON dbo.TermConcept(ParentConceptId, Level)
    INCLUDE(Code, Display)
    WHERE ParentConceptId IS NOT NULL;

GO

CREATE UNIQUE INDEX UQ_TermConcept_CodeSystem_Code
    ON dbo.TermConcept(TermCodeSystemId, Code);
