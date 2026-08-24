using Ignixa.SchemaUpgrade.Cli;
using Shouldly;

namespace Ignixa.SchemaUpgrade.Cli.Tests;

// Both tests in this class mutate/depend on process-wide state (CWD for the relative-path test)
// or an arbitrary-but-fixed absolute path (the other). xUnit v2 runs tests within the same class
// sequentially by default (parallelization happens across collections/classes, not within one),
// so the two facts below never interleave with each other. The CWD mutation is also safe against
// the other classes in this assembly (ConfirmApplyTests, RootCommandHelpTests,
// RunAsyncDataLossTests): none of them resolve any path relative to the current working
// directory, so a concurrently-running class is unaffected by this class changing it.
public class RunAsyncConfigLoadTests
{
    private const string MinimalAppSettingsJson = """
        {
          "Tenants": {
            "Mode": "Isolated",
            "Configurations": []
          }
        }
        """;

    // Proves --config threads an explicit, non-default path through to ConfigurationBuilder
    // correctly: an absolute path pointing at an arbitrary temp directory (standing in for "some
    // location the operator explicitly points at") must be found and parsed. Note: .NET's
    // FileConfigurationSource.ResolveFileProvider() resolves a rooted path correctly with or
    // without SetBasePath, so this test alone does NOT prove Program.cs:53's
    // .SetBasePath(Directory.GetCurrentDirectory()) is present -- see the relative-path test below
    // for that. No real database is needed: getting as far as a tenant-resolution failure (a
    // nonexistent tenant ID) proves the config file itself was found and parsed.
    [Fact]
    public async Task GivenAnAbsoluteConfigPath_WhenRunAsyncCalled_ThenConfigLoadsWithoutFileNotFoundException()
    {
        var tempDir = Directory.CreateTempSubdirectory("schema-upgrade-cli-config-test-");
        try
        {
            var configPath = Path.Combine(tempDir.FullName, "test-appsettings.json");
            await File.WriteAllTextAsync(configPath, MinimalAppSettingsJson);

            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();

            var options = new CliUpgradeOptions(TenantId: 999, AutoConfirm: true, AllowDataLoss: false, AllowIncompatiblePlatform: true, ConfigPath: configPath);
            var exception = await Record.ExceptionAsync(() =>
                Program.RunAsync(options, input, output, CancellationToken.None));

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

    // Proves the actual bug fix (Program.cs:53's .SetBasePath(Directory.GetCurrentDirectory())):
    // the realistic default case is a bare relative filename ("appsettings.json", matching
    // configOption's DefaultValueFactory) resolved against wherever the operator happens to run
    // the packaged tool from -- NOT the CLI assembly's own bin directory. Without SetBasePath,
    // AddJsonFile resolves a relative path against AppContext.BaseDirectory instead, so this test
    // would throw FileNotFoundException if the fix were reverted. A rooted/absolute --config path
    // (the test above) resolves correctly with or without SetBasePath, so it can't stand in for
    // this scenario.
    [Fact]
    public async Task GivenARelativeConfigPathAndAnOperatorWorkingDirectory_WhenRunAsyncCalled_ThenConfigLoadsRelativeToCurrentDirectory()
    {
        var tempDir = Directory.CreateTempSubdirectory("schema-upgrade-cli-relative-config-test-");
        var originalCurrentDirectory = Environment.CurrentDirectory;
        try
        {
            var configPath = Path.Combine(tempDir.FullName, "appsettings.json");
            await File.WriteAllTextAsync(configPath, MinimalAppSettingsJson);

            Environment.CurrentDirectory = tempDir.FullName;

            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();

            var options = new CliUpgradeOptions(TenantId: 999, AutoConfirm: true, AllowDataLoss: false, AllowIncompatiblePlatform: true, ConfigPath: "appsettings.json");
            var exception = await Record.ExceptionAsync(() =>
                Program.RunAsync(options, input, output, CancellationToken.None));

            exception.ShouldNotBeNull();
            exception.ShouldNotBeOfType<FileNotFoundException>();
            exception.ShouldBeOfType<InvalidOperationException>();
            exception.Message.ShouldContain("Tenant 999 does not exist or is inactive.");
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            Directory.Delete(tempDir.FullName, recursive: true);
        }
    }

    // Pins Program.cs's SetAction boundary catch: an unknown tenant makes RunAsync throw
    // InvalidOperationException (proven above), but System.CommandLine's default exception
    // handler would otherwise report that the same way as exit code 1 ("the operator declined
    // the confirmation prompt; nothing was applied"). Driving through CreateRootCommand/InvokeAsync
    // (not RunAsync directly, unlike the tests above) is what actually exercises that catch.
    // Deliberately does NOT redirect Console.Out/Console.Error: those are process-wide statics,
    // and RootCommandHelpTests (a different class, so a different, concurrently-running xUnit
    // collection by default) already redirects Console.Out -- swapping it here too raced with
    // that test and made it observe the wrong writer.
    [Fact]
    public async Task GivenAnUnknownTenantId_WhenInvokedThroughMain_ThenReturnsExitCode3NotExitCode1()
    {
        var tempDir = Directory.CreateTempSubdirectory("schema-upgrade-cli-exit-code-test-");
        try
        {
            var configPath = Path.Combine(tempDir.FullName, "test-appsettings.json");
            await File.WriteAllTextAsync(configPath, MinimalAppSettingsJson);

            var exitCode = await Program.CreateRootCommand()
                .Parse(["--tenant-id", "999", "--confirm", "--config", configPath])
                .InvokeAsync();

            exitCode.ShouldBe(3);
        }
        finally
        {
            Directory.Delete(tempDir.FullName, recursive: true);
        }
    }

    // Same boundary catch, different uncaught path: AddJsonFile(configPath, optional: false)
    // throws when --config does not resolve, before any tenant is even looked up. Proves the
    // catch is generic (System.IO's FileNotFoundException here, InvalidOperationException above),
    // not coincidentally tied to TenantConnectionStringResolver's exception type.
    [Fact]
    public async Task GivenAMissingConfigFile_WhenInvokedThroughMain_ThenReturnsExitCode3NotExitCode1()
    {
        var missingConfigPath = Path.Combine(Path.GetTempPath(), $"schema-upgrade-cli-missing-{Guid.NewGuid()}.json");

        var exitCode = await Program.CreateRootCommand()
            .Parse(["--tenant-id", "1", "--confirm", "--config", missingConfigPath])
            .InvokeAsync();

        exitCode.ShouldBe(3);
    }
}
