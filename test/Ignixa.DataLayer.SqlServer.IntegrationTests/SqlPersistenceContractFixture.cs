using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public sealed class SqlPersistenceContractFixture : IAsyncLifetime
{
    public TestTenantDatabase Database { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Database = await TestTenantDatabase.CreateEmptyAsync();
        await Database.ExecuteNonQueryAsync("""
            INSERT INTO dbo.Transactions
                (SurrogateIdRangeFirstValue, SurrogateIdRangeLastValue, IsVisible)
            VALUES (100, 199, 1), (200, 299, 0)
            """);
    }

    public Task DisposeAsync() => Database.DisposeAsync();
}
