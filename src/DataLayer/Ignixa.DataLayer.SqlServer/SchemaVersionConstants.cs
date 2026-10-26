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
    public const int CurrentVersion = 4;

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
    // Version 3 (expand, unreleased) -- nullable ValueSet/ConceptMap names; canonical/version replacement
    // and atomic content hashes/success logging in terminology import procedures. Canonical/version
    // columns in PackageResource, TermValueSet and TermConceptMap, and TermCodeSystem.Version, use CS_AS
    // identity comparisons and indexes. Existing procedure callers may omit the new optional ContentHash
    // parameter. No core resource tables or TVPs change.
    // Version 4 (expand) -- INCLUDE (ResourceId) on IX_Resource_ResourceTypeId_ResourceSurrgateId --
    // removes a clustered-index key lookup from chained search and _include. No column or table is dropped
    // and no data is discarded. DacFx can only express the change as DROP INDEX + CREATE INDEX (it drops
    // first even with ONLINE = ON declared, and ignores DROP_EXISTING), so both upgrade paths --
    // UpgradeIfNeededAsync and Ignixa.SchemaUpgrade.Cli -- first run ResourceSurrogateIdIndexOnlineMigration,
    // which converts the index in place with CREATE ... WITH (DROP_EXISTING = ON, ONLINE = ON) when the engine
    // supports online index operations (Azure SQL Database, Managed Instance, SQL database in Fabric,
    // Enterprise/Developer), after which the deploy finds nothing to do for it; a populated dbo.Resource
    // missing the index altogether gets it created ONLINE the same way. The build runs under an exclusive
    // sp_getapplock, so concurrent upgrades of one tenant wait for it instead of failing with error 1912.
    // It is not resumable (RESUMABLE is rejected for filtered indexes, error 10671): any interruption rolls it
    // back and the next attempt restarts from scratch. Its final Sch-M lock waits for open transactions on
    // dbo.Resource, at low priority for 5 minutes where supported (Azure SQL, SQL Server 2022+). For a large
    // dbo.Resource run the CLI before rolling out: the Web host builds before serving, and a startup probe
    // can kill it mid-build. Elsewhere (Standard/Web/Express/Edge) it logs a Warning naming the tenant, index
    // and reason, and the deploy builds the index offline: no index or uniqueness check between the DROP and
    // the CREATE, and writes to dbo.Resource blocked while it builds. Also writes
    // CH_Resource_RawResource_Length's literal as 0x00, the form SQL Server stores, ending the perpetual
    // drift that made every upgrade re-add that constraint and re-validate it WITH CHECK -- a full scan of
    // dbo.Resource under a Sch-M lock. No stored definition changes.
}
