-- Imports one ConceptMap: replaces any previous import of the same package resource, writes the concept map
-- row and its mapping elements, and records the outcome on the package resource. One call, one transaction --
-- the same reasoning as dbo.ImportTermCodeSystem.
--
-- The element rows arrive already flattened across groups, so unlike the CodeSystem import there is nothing
-- to resolve server-side; the procedure exists for the transaction, not for the work.
CREATE PROCEDURE dbo.ImportTermConceptMap
@PackageResourceId BIGINT, @Canonical NVARCHAR (512), @Version NVARCHAR (100)=NULL, @Name NVARCHAR (256)=NULL, @SourceCanonical NVARCHAR (512)=NULL, @TargetCanonical NVARCHAR (512)=NULL, @Elements dbo.TermConceptMapElementList READONLY, @ContentHash NVARCHAR (64)=NULL
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @SP AS VARCHAR (100) = 'ImportTermConceptMap', @st AS DATETIME = getUTCdate(), @InitialTranCount AS INT = @@trancount, @TermConceptMapId AS BIGINT, @Rows AS INT;
DECLARE @Mode AS VARCHAR (200) = 'PR=' + CONVERT (VARCHAR, @PackageResourceId);
BEGIN TRY
    IF @InitialTranCount = 0
        BEGIN TRANSACTION;
    UPDATE dbo.PackageResource
    SET    TerminologyImportStatus = 'InProgress',
           ImportStartDate         = SYSDATETIMEOFFSET(),
           ImportErrorMessage      = NULL
    WHERE  PackageResourceId = @PackageResourceId;
    -- Re-import replaces rather than merges. The FK from TermConceptMapElement carries ON DELETE CASCADE, so
    -- the previous elements go with the previous concept map row.
    DELETE dbo.TermConceptMap WITH (UPDLOCK, HOLDLOCK)
    WHERE  PackageResourceId = @PackageResourceId
           OR (Canonical = @Canonical AND (Version = @Version OR (Version IS NULL AND @Version IS NULL)));
    INSERT INTO dbo.TermConceptMap (PackageResourceId, Canonical, Version, Name, SourceCanonical, TargetCanonical, ImportedDate)
    VALUES                        (@PackageResourceId, @Canonical, @Version, @Name, @SourceCanonical, @TargetCanonical, SYSDATETIMEOFFSET());
    SET @TermConceptMapId = scope_identity();
    INSERT INTO dbo.TermConceptMapElement (TermConceptMapId, SourceSystemId, SourceCode, SourceDisplay, TargetSystemId, TargetCode, TargetDisplay, Equivalence, Comment, GroupIndex)
    SELECT @TermConceptMapId,
           SourceSystemId,
           SourceCode,
           SourceDisplay,
           TargetSystemId,
           TargetCode,
           TargetDisplay,
           Equivalence,
           Comment,
           GroupIndex
    FROM   @Elements;
    SET @Rows = @@rowcount;
    UPDATE dbo.PackageResource
    SET    TerminologyImportStatus = 'Completed',
           ImportCompletedDate     = SYSDATETIMEOFFSET(),
           ImportedConceptCount    = @Rows,
           ContentHash             = COALESCE(@ContentHash, ContentHash),
           ImportErrorMessage      = NULL
    WHERE  PackageResourceId = @PackageResourceId;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
    IF @InitialTranCount = 0
        COMMIT TRANSACTION;
    SELECT @TermConceptMapId;
END TRY
BEGIN CATCH
    IF @InitialTranCount = 0
       AND @@trancount > 0
        ROLLBACK;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
