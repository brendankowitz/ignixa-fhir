-- Imports one CodeSystem: replaces any previous import of the same package resource, writes the code
-- system row and all of its concepts, resolves the concept hierarchy, and records the outcome on the
-- package resource. One call, one transaction.
--
-- Why the whole import lives here rather than in the caller:
--
-- The procedure keeps replacement, hierarchy, status and content hash in one transaction and one round
-- trip. A later bookkeeping failure must not leave newly committed terminology behind a failed status.
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
@PackageResourceId BIGINT, @SystemId INT, @Version NVARCHAR (100)=NULL, @ConceptCount INT, @Content NVARCHAR (50), @IsHierarchical BIT, @CaseSensitive BIT, @Compositional BIT, @Concepts dbo.TermConceptList READONLY, @ContentHash NVARCHAR (64)=NULL
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
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
    -- The last successful import owns the canonical/version, even across package versions. Range locks
    -- serialize replacement, including an unversioned canonical with no existing row.
    DELETE dbo.TermCodeSystem WITH (UPDLOCK, HOLDLOCK)
    WHERE  PackageResourceId = @PackageResourceId
           OR (SystemId = @SystemId AND (Version = @Version OR (Version IS NULL AND @Version IS NULL)));
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
           ContentHash             = COALESCE(@ContentHash, ContentHash),
           ImportErrorMessage      = NULL
    WHERE  PackageResourceId = @PackageResourceId;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows, @Target = 'ParentLinks', @Text = @ParentRows;
    IF @InitialTranCount = 0
        COMMIT TRANSACTION;
    SELECT @TermCodeSystemId;
END TRY
BEGIN CATCH
    IF @InitialTranCount = 0
       AND @@trancount > 0
        ROLLBACK;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
