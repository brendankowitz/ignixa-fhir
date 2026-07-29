-- Imports one ValueSet: replaces any previous import of the same package resource, writes the value set
-- row and its expansion entries, and records the outcome on the package resource. One call, one
-- transaction -- the same reasoning as dbo.ImportTermCodeSystem.
--
-- IsExpanded is decided here rather than by the caller, from whether any entries arrived or the expansion
-- was known to be partial. It gates every read path: ExpandValueSetAsync and ValidateCodeAsync both filter
-- on it, so a ValueSet imported without it exists but is invisible to them. Keeping that decision beside
-- the insert stops the row and the flag from disagreeing.
--
-- Whether the entries came from an explicit expansion or from resolving compose is the caller's problem;
-- by the time they arrive here the two are indistinguishable, which is why partial-expansion state is
-- passed in rather than inferred.
CREATE PROCEDURE dbo.ImportTermValueSet
@PackageResourceId BIGINT, @Canonical NVARCHAR (512), @Version NVARCHAR (100)=NULL, @Name NVARCHAR (256), @Immutable BIT, @IsPartialExpansion BIT=0, @PartialExpansionReason NVARCHAR (1024)=NULL, @Entries dbo.TermValueSetExpansionList READONLY
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'ImportTermValueSet', @st AS DATETIME = getUTCdate(), @InitialTranCount AS INT = @@trancount, @TermValueSetId AS BIGINT, @Rows AS INT, @IsExpanded AS BIT;
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
    SET @IsExpanded = CASE WHEN @Rows > 0
                                OR @IsPartialExpansion = 1 THEN 1 ELSE 0 END;
    INSERT INTO dbo.TermValueSet (PackageResourceId, Canonical, Version, Name, Immutable, IsExpanded, LastExpansionDate, ExpansionCodeCount, IsPartialExpansion, PartialExpansionReason, ImportedDate)
    VALUES                      (@PackageResourceId, @Canonical, @Version, @Name, @Immutable, @IsExpanded, CASE WHEN @IsExpanded = 1 THEN SYSDATETIMEOFFSET() ELSE NULL END, @Rows, @IsPartialExpansion, @PartialExpansionReason, SYSDATETIMEOFFSET());
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
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows, @Target = 'IsExpanded', @Text = @IsExpanded;
    SELECT @TermValueSetId;
END TRY
BEGIN CATCH
    IF @InitialTranCount = 0
       AND @@trancount > 0
        ROLLBACK;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
