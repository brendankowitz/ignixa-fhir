using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests.Features.Terminology;

/// <summary>
/// Pins that <see cref="SqlServerTerminologyImporterFactory"/> rejects a non-positive
/// <c>commandTimeoutSeconds</c> at construction rather than deferring the failure to
/// <see cref="SqlServerTerminologyImporterFactory.CreateAsync"/>, where the same value is forwarded into
/// <see cref="SqlServerCodeSystemImporter"/>. Failing here means a misconfigured
/// <c>TerminologyImportCommandTimeoutSeconds</c> option surfaces at composition-root registration time
/// rather than on the first terminology import attempt.
/// </summary>
public class SqlServerTerminologyImporterFactoryCommandTimeoutTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenANonPositiveCommandTimeout_WhenConstructed_ThenItThrows(int commandTimeoutSeconds)
    {
        var error = Should.Throw<ArgumentOutOfRangeException>(() => new SqlServerTerminologyImporterFactory(
            sqlExecutionService: null!,
            cacheRegistry: null!,
            systemPartitionId: 0,
            loggerFactory: null!,
            commandTimeoutSeconds));

        error.ParamName.ShouldBe("commandTimeoutSeconds");
    }
}
