using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

public sealed class TerminologyReplacementFixture : IAsyncLifetime
{
    public TerminologyTestFixture Database { get; private set; } = null!;

    public async Task InitializeAsync() => Database = await TerminologyTestFixture.CreateAsync();

    public async Task DisposeAsync() => await Database.DisposeAsync();
}
