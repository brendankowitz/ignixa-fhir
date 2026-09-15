namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

internal sealed class LastNThrowingSchemaVersionResolver : ISchemaVersionResolver
{
    public Task<int> GetCurrentVersionAsync(int tenantId, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Not expected to be called by DeployIfEmptyAsync.");
}
