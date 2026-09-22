using Xunit;

namespace CompatibilityContractFixtures;

public class RequiredContractTests
{
    [Fact]
    public void PassingSqlServerJson() => Assert.True(true);

    [Fact]
    public void FailingRequiredSqlServerJson() => Assert.Fail("Synthetic required contract failure.");

    [Fact(Skip = "Synthetic explicitly unsupported contract.")]
    public void SkippedSqlServerJson() => Assert.True(true);

    [Fact]
    public void ImportSqlServerJson() => Assert.Fail("Synthetic excluded import category.");
}
