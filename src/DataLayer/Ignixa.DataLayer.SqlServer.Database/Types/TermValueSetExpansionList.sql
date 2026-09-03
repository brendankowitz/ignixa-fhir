-- Expansion entries for a single ValueSet import.
--
-- SystemId rather than a system URI: unlike concept parents, these ids already exist by the time the
-- caller builds the list -- it resolves each system through ISystemRepository while walking the expansion
-- or compose, so passing the id avoids making the procedure repeat that work.
--
-- Ordinal is carried rather than derived: the caller assigns it while building the set, and for a compose
-- expansion that order reflects include processing rather than anything recoverable from the rows.
--
-- The code column(s) mirror their destination table's collation; see TermConcept.sql.
CREATE TYPE dbo.TermValueSetExpansionList AS TABLE (
    SystemId      INT            NOT NULL,
    Code          NVARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Display       NVARCHAR (500) NULL,
    SystemVersion NVARCHAR (100) NULL,
    IsActive      BIT            NOT NULL,
    Ordinal       INT            NOT NULL,
    INDEX IX_TermValueSetExpansionList_Code NONCLUSTERED (SystemId, Code));
