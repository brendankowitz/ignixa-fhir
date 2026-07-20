namespace Ignixa.DataLayer.SqlServer;

public interface ISchemaDeployer
{
    /// <summary>
    /// Deploys the schema to a tenant's database if -- and only if -- that database is
    /// currently empty. Never modifies a database that already has schema.
    /// </summary>
    Task DeployIfEmptyAsync(int tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Upgrades a tenant's existing, non-empty database to the current schema version if it's
    /// behind and the pending diff is provably safe to auto-apply (no operations outside
    /// DeployReportClassifier's allow-list). Throws if the tenant is behind and the diff is
    /// NOT auto-safe -- the caller must use the operator-triggered CLI path instead. No-ops if
    /// the tenant is already current.
    /// </summary>
    Task UpgradeIfNeededAsync(int tenantId, CancellationToken cancellationToken);
}
