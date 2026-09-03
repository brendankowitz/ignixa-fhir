namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// This project's compiled-in schema-version window. Bumped by whoever authors a real
/// schema change, alongside an expand/contract classification recorded in the changelog
/// below -- mirrors fhir-server's SchemaVersionConstants pattern, adapted for Ignixa's
/// per-tenant (not single-shared-database) versioning model.
/// </summary>
public static class SchemaVersionConstants
{
    /// <summary>The schema version this build's dacpac represents.</summary>
    public const int CurrentVersion = 2;

    /// <summary>
    /// The oldest tenant schema version this build still tolerates reading an
    /// un-upgraded tenant against. No version-gated read/write behavior exists yet
    /// (Phase D/E's job) -- this constant is the primitive future code will check.
    /// </summary>
    public const int MinSupportedReadVersion = 1;

    // Changelog (append, never edit history):
    // Version 1 (expand) -- introduces the SchemaVersion table itself. Every tenant
    // database, new or upgraded, starts here.
    // Version 2 (expand) -- terminology import and code-collation work. Adds
    // dbo.ImportTermValueSet and dbo.ImportTermConceptMap, the dbo.TermValueSetExpansionList
    // and dbo.TermConceptMapElementList table types, and rewrites dbo.ImportTermCodeSystem and
    // dbo.TermConceptList; makes every terminology code column and dbo.System.Value
    // Latin1_General_100_CS_AS (a rebuild of dbo.System, whose clustered key is Value, and an
    // index rebuild on TermConcept/TermConceptMapElement/TermValueSetExpansion); sets
    // LOCK_ESCALATION = AUTO on the six terminology tables; retargets the project's DSP to
    // SqlAzureV12; and makes Script.PostDeployment.sql's partition splitting per-boundary
    // resumable. No column or table is dropped and no data is discarded --
    // DeployReportClassifier reports the whole diff as AutoSafe with a DataMotion alert
    // (dbo.System and dbo.ResourceChangeData are rebuilt in place) and no DataIssue, so the
    // automatic path applies it; see DdlSchemaVersionBumpGuardTests for what forces this entry.
}
