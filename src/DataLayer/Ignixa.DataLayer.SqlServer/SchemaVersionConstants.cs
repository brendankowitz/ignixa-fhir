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
    public const int CurrentVersion = 1;

    /// <summary>
    /// The oldest tenant schema version this build still tolerates reading an
    /// un-upgraded tenant against. No version-gated read/write behavior exists yet
    /// (Phase D/E's job) -- this constant is the primitive future code will check.
    /// </summary>
    public const int MinSupportedReadVersion = 1;

    // Changelog (append, never edit history):
    // Version 1 (expand) -- introduces the SchemaVersion table itself. Every tenant
    // database, new or upgraded, starts here.
}
