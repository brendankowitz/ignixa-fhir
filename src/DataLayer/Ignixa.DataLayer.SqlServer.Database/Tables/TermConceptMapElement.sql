CREATE TABLE dbo.TermConceptMapElement (
    TermConceptMapElementId BIGINT NOT NULL IDENTITY (1, 1),
    TermConceptMapId BIGINT NOT NULL,
    SourceSystemId INT NOT NULL,
    SourceCode NVARCHAR (256) NOT NULL,
    SourceDisplay NVARCHAR (500) NULL,
    TargetSystemId INT NULL,
    TargetCode NVARCHAR (256) NULL,
    TargetDisplay NVARCHAR (500) NULL,
    Equivalence NVARCHAR (50) NOT NULL,
    Comment NVARCHAR (MAX) NULL,
    GroupIndex INT NOT NULL,
    CONSTRAINT PK_TermConceptMapElement PRIMARY KEY (TermConceptMapElementId),
    CONSTRAINT FK_TermConceptMapElement_ConceptMap FOREIGN KEY (TermConceptMapId) REFERENCES dbo.TermConceptMap (TermConceptMapId) ON DELETE CASCADE,
    CONSTRAINT FK_TermConceptMapElement_SourceSystem FOREIGN KEY (SourceSystemId) REFERENCES dbo.System (SystemId),
    CONSTRAINT FK_TermConceptMapElement_TargetSystem FOREIGN KEY (TargetSystemId) REFERENCES dbo.System (SystemId)
);

GO

-- Convention-only here, not load-bearing: this table is not partitioned, so AUTO escalates to TABLE
-- level exactly like SQL Server's un-set default. See TermCodeSystem.sql for the full explanation.
ALTER TABLE dbo.TermConceptMapElement SET (LOCK_ESCALATION = AUTO);

GO

CREATE INDEX IX_TermConceptMapElement_Source
    ON dbo.TermConceptMapElement(SourceSystemId, SourceCode)
    INCLUDE(TermConceptMapId, TargetSystemId, TargetCode, Equivalence);

GO

CREATE INDEX IX_TermConceptMapElement_Target
    ON dbo.TermConceptMapElement(TargetSystemId, TargetCode)
    INCLUDE(TermConceptMapId, SourceSystemId, SourceCode, Equivalence)
    WHERE TargetSystemId IS NOT NULL;

GO

CREATE INDEX IX_TermConceptMapElement_TermConceptMapId
    ON dbo.TermConceptMapElement(TermConceptMapId);
