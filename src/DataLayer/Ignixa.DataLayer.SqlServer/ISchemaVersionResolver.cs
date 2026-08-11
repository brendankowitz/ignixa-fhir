namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Reads a tenant's currently-applied schema version. Used today by
/// <see cref="SchemaDeployer.UpgradeIfNeededAsync"/> to decide whether a tenant is behind and an
/// upgrade is needed. This is also the version-gating primitive Phase D/E's future
/// version-dependent read/write code will call to decide which SQL shape to use for a given
/// tenant -- that specific consumer doesn't exist yet, but the interface is already load-bearing
/// for schema upgrades, not merely a future-facing stub.
/// </summary>
public interface ISchemaVersionResolver
{
    /// <summary>Returns the tenant's currently-applied schema version, or 0 if untracked
    /// (a pre-Phase-C tenant that predates the SchemaVersion table).</summary>
    Task<int> GetCurrentVersionAsync(int tenantId, CancellationToken cancellationToken);
}
