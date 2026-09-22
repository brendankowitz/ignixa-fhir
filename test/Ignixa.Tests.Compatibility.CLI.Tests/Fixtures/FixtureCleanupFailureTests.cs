using Xunit;

namespace CompatibilityContractFixtures;

public class FixtureCleanupFailureTests : IClassFixture<CleanupFailureFixture>
{
    [Fact]
    public void PassingBeforeCleanupSqlServerJson() => Assert.True(true);
}
