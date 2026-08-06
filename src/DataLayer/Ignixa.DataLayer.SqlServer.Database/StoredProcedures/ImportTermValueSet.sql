-- Imports one ValueSet: replaces any previous import of the same package resource, writes the value set
-- row and its expansion entries, and records the outcome on the package resource. One call, one
-- transaction -- the same reasoning as dbo.ImportTermCodeSystem.
--
-- IsExpanded records that an expansion ran, not that it produced codes. Reaching this procedure is the
-- expansion having run, so the flag is 1 unconditionally. Deriving it from the row count conflated "nobody
-- expanded this" with "the expansion is correctly empty" -- a compose whose excludes remove every included
-- code, or one that designates nothing at all. It gates every read path: ExpandValueSetAsync and
-- ValidateCodeAsync both filter on it, so that second case became a ValueSet those operations report as
-- missing rather than as an honest empty expansion.
--
-- Whether the entries came from an explicit expansion or from resolving compose is the caller's problem;
-- by the time they arrive here the two are indistinguishable, which is why partial-expansion state is
-- passed in rather than inferred.
CREATE PROCEDURE dbo.ImportTermValueSet
@PackageResourceId BIGINT, @Canonical NVARCHAR (512), @Version NVARCHAR (100)=NULL, @Name NVARCHAR (256), @Immutable BIT, @IsPartialExpansion BIT=0, @PartialExpansionReason NVARCHAR (1024)=NULL, @Entries dbo.TermValueSetExpansionList READONLY
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'ImportTermValueSet', @st AS DATETIME = getUTCdate(), @InitialTranCount AS INT = @@trancount, @TermValueSetId AS BIGINT, @Rows AS INT;
DECLARE @Mode AS VARCHAR (200) = 'PR=' + CONVERT (VARCHAR, @PackageResourceId);
BEGIN TRY
    IF @InitialTranCount = 0
        BEGIN TRANSACTION;
    UPDATE dbo.PackageResource
    SET    TerminologyImportStatus = 'InProgress',
           ImportStartDate         = SYSDATETIMEOFFSET(),
           ImportErrorMessage      = NULL
    WHERE  PackageResourceId = @PackageResourceId;
    -- Re-import replaces rather than merges. The FK from TermValueSetExpansion carries ON DELETE CASCADE,
    -- so the previous expansion goes with the previous value set row.
    DELETE dbo.TermValueSet
    WHERE  PackageResourceId = @PackageResourceId;
    SET @Rows = (SELECT COUNT(*)
                 FROM   @Entries);
    INSERT INTO dbo.TermValueSet (PackageResourceId, Canonical, Version, Name, Immutable, IsExpanded, LastExpansionDate, ExpansionCodeCount, IsPartialExpansion, PartialExpansionReason, ImportedDate)
    VALUES                      (@PackageResourceId, @Canonical, @Version, @Name, @Immutable, 1, SYSDATETIMEOFFSET(), @Rows, @IsPartialExpansion, @PartialExpansionReason, SYSDATETIMEOFFSET());
    SET @TermValueSetId = scope_identity();
    INSERT INTO dbo.TermValueSetExpansion (TermValueSetId, SystemId, Code, Display, SystemVersion, IsActive, Ordinal)
    SELECT @TermValueSetId,
           SystemId,
           Code,
           Display,
           SystemVersion,
           IsActive,
           Ordinal
    FROM   @Entries;
    UPDATE dbo.PackageResource
    SET    TerminologyImportStatus = 'Completed',
           ImportCompletedDate     = SYSDATETIMEOFFSET(),
           ImportedConceptCount    = @Rows,
           ImportErrorMessage      = NULL
    WHERE  PackageResourceId = @PackageResourceId;
    IF @InitialTranCount = 0
        COMMIT TRANSACTION;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows, @Target = 'IsPartialExpansion', @Text = @IsPartialExpansion;
    SELECT @TermValueSetId;
END TRY
BEGIN CATCH
    IF @InitialTranCount = 0
       AND @@trancount > 0
        ROLLBACK;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
