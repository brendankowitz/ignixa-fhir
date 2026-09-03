-- Imports one CodeSystem: replaces any previous import of the same package resource, writes the code
-- system row and all of its concepts, resolves the concept hierarchy, and records the outcome on the
-- package resource. One call, one transaction.
--
-- Why the whole import lives here rather than in the caller:
--
-- ISqlExecutionService opens a fresh connection per call and exposes no transaction API, so a client-side
-- import spanning several calls cannot be atomic. The implementation this replaces got atomicity from an
-- EF transaction wrapping all of its steps; without that, a failure between steps would leave a
-- TermCodeSystem with no concepts, or concepts with no hierarchy, and the package row still claiming
-- InProgress. Moving the sequence into one procedure restores the guarantee the port would otherwise lose.
--
-- It also removes a defect rather than reproducing it. The previous design inserted concepts by one of two
-- client-side paths -- EF AddRange at or below 1,000 concepts, SqlBulkCopy above it -- and only the bulk
-- path ran the separate parent-resolution pass. Every smaller CodeSystem therefore imported with a flat
-- hierarchy, and $subsumes answered "not-subsumed" for every pair in it while returning a well-formed FHIR
-- response. A single path cannot forget to run its own second half.
--
-- The parent pass previously used a #temp table, which could not survive the port either: connection-pool
-- reuse issues sp_reset_connection, dropping session-scoped temp objects even on the same SPID. A
-- table-valued parameter is a parameter, not session state.
CREATE PROCEDURE dbo.ImportTermCodeSystem
@PackageResourceId BIGINT, @SystemId INT, @Version NVARCHAR (100)=NULL, @ConceptCount INT, @Content NVARCHAR (50), @IsHierarchical BIT, @CaseSensitive BIT, @Compositional BIT, @Concepts dbo.TermConceptList READONLY
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'ImportTermCodeSystem', @st AS DATETIME = getUTCdate(), @InitialTranCount AS INT = @@trancount, @TermCodeSystemId AS BIGINT, @Rows AS INT, @ParentRows AS INT;
DECLARE @Mode AS VARCHAR (200) = 'PR=' + CONVERT (VARCHAR, @PackageResourceId) + ' S=' + CONVERT (VARCHAR, @SystemId);
BEGIN TRY
    -- Composes with a caller's transaction rather than assuming ownership, matching the MergeResources
    -- family.
    IF @InitialTranCount = 0
        BEGIN TRANSACTION;
    UPDATE dbo.PackageResource
    SET    TerminologyImportStatus = 'InProgress',
           ImportStartDate         = SYSDATETIMEOFFSET(),
           ImportErrorMessage      = NULL
    WHERE  PackageResourceId = @PackageResourceId;
    -- Re-import replaces rather than merges. The FK from TermConcept carries ON DELETE CASCADE, so the old
    -- concepts go with the old code system row and cannot survive as orphans of a previous version.
    DELETE dbo.TermCodeSystem
    WHERE  PackageResourceId = @PackageResourceId;
    INSERT INTO dbo.TermCodeSystem (PackageResourceId, SystemId, Version, ConceptCount, Content, IsHierarchical, CaseSensitive, Compositional, ImportedDate)
    VALUES                        (@PackageResourceId, @SystemId, @Version, @ConceptCount, @Content, @IsHierarchical, @CaseSensitive, @Compositional, SYSDATETIMEOFFSET());
    SET @TermCodeSystemId = scope_identity();
    INSERT INTO dbo.TermConcept (TermCodeSystemId, Code, Display, Definition, ParentConceptId, Level, IsActive, PropertiesJson)
    SELECT @TermCodeSystemId,
           Code,
           Display,
           Definition,
           NULL,
           Level,
           IsActive,
           PropertiesJson
    FROM   @Concepts;
    SET @Rows = @@rowcount;
    -- Parent ids only exist after the insert above, which is why the caller sends parent CODES. Scoped to
    -- this code system on both sides, so a code shared with another system cannot link across them.
    UPDATE child
    SET    child.ParentConceptId = parent.TermConceptId
    FROM   dbo.TermConcept AS child
           INNER JOIN
           @Concepts AS src
           ON src.Code = child.Code
           INNER JOIN
           dbo.TermConcept AS parent
           ON parent.TermCodeSystemId = @TermCodeSystemId
              AND parent.Code = src.ParentCode
    WHERE  child.TermCodeSystemId = @TermCodeSystemId
           AND src.ParentCode IS NOT NULL;
    SET @ParentRows = @@rowcount;
    UPDATE dbo.PackageResource
    SET    TerminologyImportStatus = 'Completed',
           ImportCompletedDate     = SYSDATETIMEOFFSET(),
           ImportedConceptCount    = @Rows,
           ImportErrorMessage      = NULL
    WHERE  PackageResourceId = @PackageResourceId;
    IF @InitialTranCount = 0
        COMMIT TRANSACTION;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows, @Target = 'ParentLinks', @Text = @ParentRows;
    SELECT @TermCodeSystemId;
END TRY
BEGIN CATCH
    IF @InitialTranCount = 0
       AND @@trancount > 0
        ROLLBACK;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
