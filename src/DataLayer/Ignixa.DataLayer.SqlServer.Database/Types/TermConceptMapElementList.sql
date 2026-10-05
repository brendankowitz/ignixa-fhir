-- Mapping elements for a single ConceptMap import, flattened across all of its groups.
--
-- The group is not a table of its own: GroupIndex carries which group each element came from, which is all
-- dbo.TermConceptMapElement stores. Source and target system ids are already resolved by the caller through
-- ISystemRepository, so the procedure never has to look at a URI.
--
-- TargetSystemId is nullable and means "no target system", which is not the same as "no target". An element
-- can carry a target code whose system the ConceptMap never declared; storing a placeholder id instead would
-- make it look like a real system on the read path.
--
-- The code column(s) mirror their destination table's collation; see TermConcept.sql.
CREATE TYPE dbo.TermConceptMapElementList AS TABLE (
    SourceSystemId INT            NOT NULL,
    SourceCode     NVARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SourceDisplay  NVARCHAR (500) NULL,
    TargetSystemId INT            NULL,
    TargetCode     NVARCHAR (256) COLLATE Latin1_General_100_CS_AS NULL,
    TargetDisplay  NVARCHAR (500) NULL,
    Equivalence    NVARCHAR (50)  NOT NULL,
    Comment        NVARCHAR (MAX) NULL,
    GroupIndex     INT            NOT NULL,
    INDEX IX_TermConceptMapElementList_Source NONCLUSTERED (SourceSystemId, SourceCode));
