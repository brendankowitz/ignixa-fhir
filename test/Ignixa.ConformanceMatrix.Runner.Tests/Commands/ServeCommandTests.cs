using Ignixa.ConformanceMatrix.Runner.Commands;
using Ignixa.ConformanceMatrix.Runner.Tests.Serving;
using Shouldly;

namespace Ignixa.ConformanceMatrix.Runner.Tests.Commands;

public class ServeCommandTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("10.0.0.5", false)]
    [InlineData("not-an-ip", false)]
    public void GivenHostIp_WhenClassifyingLoopback_ThenOnlyLoopbackAddressesPass(string hostIp, bool expected)
    {
        ServeCommand.IsLoopbackHost(hostIp).ShouldBe(expected);
    }

    [Fact]
    public async Task GivenNonLoopbackHostIp_WithoutAllowRemoteHosts_WhenServing_ThenRefusesToStart()
    {
        // Arrange
        using var dir = new TempTestsDirectory();
        dir.WriteScript("Good.json", TempTestsDirectory.ValidScriptJson("Good"));

        // Act
        var exitCode = await ServeCommand.RunAsync(
            dir.Root, port: 5599, hostIp: "0.0.0.0", allowRemoteHosts: false,
            fhirVersion: null, authHeader: null, CancellationToken.None);

        // Assert
        exitCode.ShouldBe(ExitCodes.UsageError);
    }

    [Fact]
    public async Task GivenMissingTestsDirectory_WhenServing_ThenExitsWithUsageError()
    {
        // Act
        var exitCode = await ServeCommand.RunAsync(
            Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}"),
            port: 5599, hostIp: "127.0.0.1", allowRemoteHosts: false,
            fhirVersion: null, authHeader: null, CancellationToken.None);

        // Assert
        exitCode.ShouldBe(ExitCodes.UsageError);
    }
}
