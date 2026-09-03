-- Concepts for a single CodeSystem import, carrying the parent link as a CODE rather than an id.
--
-- The client cannot know parent ids: they are IDENTITY values assigned by the insert itself. Sending the
-- parent code instead lets dbo.ImportTermConcepts resolve the links server-side in one set-based pass,
-- which is what removes the previous implementation's separate second pass -- and with it the bug where
-- that pass only ran for CodeSystems above its 1,000-concept threshold.
--
-- Indexed rather than keyed on Code: a CodeSystem with duplicate codes is malformed, but the previous
-- implementation inserted such rows rather than failing, and this type is not the place to start rejecting
-- them.
--
-- Code and ParentCode must carry the SAME collation as dbo.TermConcept.Code (see TermConcept.sql). This is
-- not cosmetic: the parent-resolution pass in dbo.ImportTermCodeSystem joins `src.Code = child.Code` and
-- `parent.Code = src.ParentCode` across this type and that table, and joining two columns whose implicit
-- collations differ is error 468, which would fail every CodeSystem import outright.
CREATE TYPE dbo.TermConceptList AS TABLE (
    Code           NVARCHAR (256)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    Display        NVARCHAR (500)  NULL,
    Definition     NVARCHAR (4000) NULL,
    ParentCode     NVARCHAR (256)  COLLATE Latin1_General_100_CS_AS NULL,
    Level          INT             NOT NULL,
    IsActive       BIT             NOT NULL,
    PropertiesJson NVARCHAR (MAX)  NULL,
    INDEX IX_TermConceptList_Code NONCLUSTERED (Code),
    INDEX IX_TermConceptList_ParentCode NONCLUSTERED (ParentCode));
