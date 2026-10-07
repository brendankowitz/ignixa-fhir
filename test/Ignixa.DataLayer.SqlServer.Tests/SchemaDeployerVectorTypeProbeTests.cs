using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests;

/// <summary>
/// Pins <see cref="SchemaDeployer.EnsureVectorTypeSupported"/> -- the pure decision behind the probe
/// both <c>DeployIfEmptyAsync</c> and <c>UpgradeIfNeededAsync</c> run before loading the schema version 5
/// dacpac, which has a <c>vector</c>-typed column. Tested with a substituted probe result rather than a
/// real engine, matching <see cref="SchemaDeployerDeployOptionsTests"/>'s reason for
/// <see cref="SchemaDeployer.CreateDeployOptions"/> being internal rather than private.
/// </summary>
public class SchemaDeployerVectorTypeProbeTests
{
    [Fact]
    public void GivenAnEngineWithoutVectorType_WhenProbed_ThenFailsWithEngineMessage()
    {
        var ex = Should.Throw<InvalidOperationException>(() => SchemaDeployer.EnsureVectorTypeSupported(vectorTypeCount: 0));

        ex.Message.ShouldBe(
            "Ignixa schema version 5 requires a SQL engine with the native vector type (Azure SQL Database or SQL Server 2025+).");
    }

    [Fact]
    public void GivenAnEngineWithVectorType_WhenProbed_ThenDoesNotThrow()
    {
        Should.NotThrow(() => SchemaDeployer.EnsureVectorTypeSupported(vectorTypeCount: 1));
    }
}
