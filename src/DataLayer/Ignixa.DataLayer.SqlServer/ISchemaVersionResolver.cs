namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Reads a tenant's currently-applied schema version. This is the version-gating
/// primitive -- Phase D/E's future version-dependent read/write code will call this to
/// decide which SQL shape to use for a given tenant. No real caller exists yet.
/// </summary>
public interface ISchemaVersionResolver
{
    /// <summary>Returns the tenant's currently-applied schema version, or 0 if untracked
    /// (a pre-Phase-C tenant that predates the SchemaVersion table).</summary>
    Task<int> GetCurrentVersionAsync(int tenantId, CancellationToken cancellationToken);
}
