-- Code carries an explicit COLLATE, and every other terminology code column (and the table types feeding
-- them) carries the same one, pointing here.
--
-- Without it the column takes the database's default collation, which is case-INSENSITIVE on a stock SQL
-- Server and on Azure SQL Database alike -- so `Code = @code` matched a code the CodeSystem does not
-- contain. The import path never agreed with that: SqlServerValueSetComposer filters concepts in C# with
-- StringComparison.Ordinal, so the same code was matched case-sensitively when written and
-- case-insensitively when read. For UCUM that is a conformance defect rather than a nicety -- `mg` and `MG`
-- are different units, `Gy` and `gy` likewise.
--
-- Case-sensitive is also the only storage that can represent both kinds of CodeSystem. Under a
-- case-insensitive collation UQ_TermConcept_CodeSystem_Code below cannot hold both `AB` and `ab`, so a
-- CodeSystem legitimately containing the pair fails to import at all; under a case-sensitive one, the
-- case-insensitive matching a caseSensitive=false CodeSystem asks for is layered back on at read time,
-- gated on dbo.TermCodeSystem.CaseSensitive. See SqlServerTerminologyService.CaseInsensitiveCollation.
--
-- Matches what the search-parameter tables already do: TokenSearchParam.Code, QuantityCode.Value and the
-- rest are CS_AS, so terminology was the one place in this schema where a FHIR code was compared
-- case-insensitively.
CREATE TABLE dbo.TermConcept (
    TermConceptId    BIGINT         NOT NULL IDENTITY (1, 1),
    TermCodeSystemId BIGINT         NOT NULL,
    Code             NVARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
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
