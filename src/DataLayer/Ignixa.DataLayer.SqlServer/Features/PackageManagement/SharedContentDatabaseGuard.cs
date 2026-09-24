using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer.Features.PackageManagement;

/// <summary>
/// PackageResource identities are database-local. Package storage and terminology must resolve to the
/// same SQL database before either side can mutate shared content.
/// </summary>
public sealed class SharedContentDatabaseGuard(
    ISqlExecutionService sqlExecutionService,
    int packageTenantId,
    int terminologyPartitionId)
{
    public async Task EnsureCompatibleAsync(CancellationToken cancellationToken)
    {
        if (packageTenantId == terminologyPartitionId)
        {
            return;
        }

        await sqlExecutionService.ExecuteInTransactionAsync(
            packageTenantId,
            async (transaction, ct) =>
            {
                // An unpredictable database-local lock proves identity across aliases and credentials.
                // Server/database names alone can collide on independent SQL instances. No rows are written,
                // and commit/rollback releases the probe lock before any content operation starts.
                var resource = $"Ignixa.SharedContent.{Guid.NewGuid():N}";
                using var acquire = new SqlCommand("""
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = 'Exclusive',
                        @LockOwner = 'Transaction', @DbPrincipal = 'public', @LockTimeout = 0;
                    SELECT @result;
                    """);
                acquire.Parameters.AddWithValue("@resource", resource);
                var acquired = await transaction.ExecuteReaderAsync(acquire, reader => reader.GetInt32(0), ct);
                if (acquired.Single() < 0)
                {
                    throw new InvalidOperationException("Unable to acquire the shared-content database identity probe.");
                }

                using var probe = new SqlCommand("SELECT APPLOCK_TEST('public', @resource, 'Shared', 'Session')");
                probe.Parameters.AddWithValue("@resource", resource);
                var grantable = await sqlExecutionService.ExecuteReaderAsync(
                    terminologyPartitionId, probe, reader => reader.GetInt32(0), ct);
                if (grantable.Single() != 0)
                {
                    throw new InvalidOperationException(
                        $"Package tenant {packageTenantId} and terminology partition {terminologyPartitionId} must use the same shared SQL database. " +
                        "Configure the system partition to inherit the package tenant's connection string, or point both at the same database. " +
                        "Separate package and terminology databases are not supported; no shared-content mutation was performed.");
                }
            },
            cancellationToken);
    }
}
