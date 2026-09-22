using System;
using System.Threading.Tasks;
using Xunit;

namespace CompatibilityContractFixtures;

public sealed class CleanupFailureFixture : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.FromException(new InvalidOperationException("Synthetic fixture cleanup failure."));
}
