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
CREATE TYPE dbo.TermConceptList AS TABLE (
    Code           NVARCHAR (256)  NOT NULL,
    Display        NVARCHAR (500)  NULL,
    Definition     NVARCHAR (4000) NULL,
    ParentCode     NVARCHAR (256)  NULL,
    Level          INT             NOT NULL,
    IsActive       BIT             NOT NULL,
    PropertiesJson NVARCHAR (MAX)  NULL,
    INDEX IX_TermConceptList_Code NONCLUSTERED (Code),
    INDEX IX_TermConceptList_ParentCode NONCLUSTERED (ParentCode));
