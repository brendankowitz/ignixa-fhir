using Ignixa.SchemaUpgrade.Cli;
using Shouldly;

namespace Ignixa.SchemaUpgrade.Cli.Tests;

public class RunAsyncConfigLoadTests
{
    // Proves the Program.cs:44-48 bug (Finding 2) is fixed: AddJsonFile used to resolve
    // "appsettings.json" relative to AppContext.BaseDirectory (the test assembly's own bin
    // folder) with no SetBasePath, so a --config path pointing anywhere else -- an arbitrary
    // temp directory here, standing in for "wherever the operator happens to run the packaged
    // tool from" -- would throw FileNotFoundException before ever reaching tenant resolution.
    // No real database is needed: getting as far as a tenant-resolution failure (a nonexistent
    // tenant ID) proves the config file itself was found and parsed.
    [Fact]
    public async Task GivenConfigPathPointingAtAnArbitraryDirectory_WhenRunAsyncCalled_ThenConfigLoadsWithoutFileNotFoundException()
    {
        var tempDir = Directory.CreateTempSubdirectory("schema-upgrade-cli-config-test-");
        try
        {
            var configPath = Path.Combine(tempDir.FullName, "test-appsettings.json");
            await File.WriteAllTextAsync(configPath, """
                {
                  "Tenants": {
                    "Mode": "Isolated",
                    "Configurations": []
                  }
                }
                """);

            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();

            var exception = await Record.ExceptionAsync(() =>
                Program.RunAsync(tenantId: 999, autoConfirm: true, allowDataLoss: false, configPath, input, output, CancellationToken.None));

            exception.ShouldNotBeNull();
            exception.ShouldNotBeOfType<FileNotFoundException>();
            exception.ShouldBeOfType<InvalidOperationException>();
            exception.Message.ShouldContain("Tenant 999 does not exist or is inactive.");
        }
        finally
        {
            Directory.Delete(tempDir.FullName, recursive: true);
        }
    }
}
