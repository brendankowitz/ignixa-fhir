namespace Ignixa.DataLayer.SqlServer;

public interface ISchemaDeployer
{
    /// <summary>
    /// Deploys the schema to a tenant's database if -- and only if -- that database is
    /// currently empty. Never modifies a database that already has schema.
    /// </summary>
    Task DeployIfEmptyAsync(int tenantId, CancellationToken cancellationToken);
}
