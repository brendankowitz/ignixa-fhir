// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.ConformanceMatrix.Cli.Commands;
using Ignixa.TestScript.Locust.Diagnostics;
using Ignixa.TestScript.Locust.Ir;

namespace Ignixa.ConformanceMatrix.Cli.Tests;

public sealed class CompileLocustCommandTests : IDisposable
{
    private const string MinimalSuccessJson = """{"resourceType":"TestScript","name":"Basic","status":"active"}""";

    private readonly string _root;

    public CompileLocustCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "compile-locust-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task<string> WriteTestScriptAsync(string json, string fileName = "test.json")
    {
        var path = Path.Combine(_root, fileName);
        await File.WriteAllTextAsync(path, json);
        return path;
    }

    private string NewOutDir(string name = "out") => Path.Combine(_root, name);

    private static async Task SeedSentinelDirectoryAsync(string outDir)
    {
        Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(Path.Combine(outDir, "sentinel.txt"), "sentinel");
    }

    private static async Task<(int ExitCode, string Output, string Error)> InvokeAsync(params string[] args)
    {
        var command = CompileLocustCommand.Build();
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var configuration = new InvocationConfiguration
        {
            Output = outputWriter,
            Error = errorWriter
        };

        var exitCode = await command.Parse(args).InvokeAsync(configuration);
        return (exitCode, outputWriter.ToString(), errorWriter.ToString());
    }

    // ------------------------------------------------------------------------------------------
    // 1. Actual Build() invocation: missing required options -> 2
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task GivenMissingTestOption_WhenInvokingActualCommand_ThenReturnsUsageErrorExitCode()
    {
        var outDir = NewOutDir();

        var (exitCode, _, error) = await InvokeAsync("--out", outDir, "--fhir-version", "4.0");

        exitCode.ShouldBe(2);
        error.ShouldContain("--test");
    }

    [Fact]
    public async Task GivenMissingOutOption_WhenInvokingActualCommand_ThenReturnsUsageErrorExitCode()
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);

        var (exitCode, _, error) = await InvokeAsync("--test", testPath, "--fhir-version", "4.0");

        exitCode.ShouldBe(2);
        error.ShouldContain("--out");
    }

    [Fact]
    public async Task GivenMissingFhirVersionOption_WhenInvokingActualCommand_ThenReturnsUsageErrorExitCode()
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);
        var outDir = NewOutDir();

        var (exitCode, _, error) = await InvokeAsync("--test", testPath, "--out", outDir);

        exitCode.ShouldBe(2);
        error.ShouldContain("--fhir-version");
    }

    // ------------------------------------------------------------------------------------------
    // 1b. Actual Build() invocation: dangling flag values (present, no following value) -> 2
    //     rather than System.CommandLine's own default arity-error exit code.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task GivenDanglingTestFlag_WhenInvokingActualCommand_ThenReturnsUsageErrorExitCode()
    {
        var outDir = NewOutDir();

        var (exitCode, _, error) = await InvokeAsync("--test", "--out", outDir, "--fhir-version", "4.0");

        exitCode.ShouldBe(2);
        error.ShouldContain("--test");
    }

    [Fact]
    public async Task GivenDanglingOutFlag_WhenInvokingActualCommand_ThenReturnsUsageErrorExitCode()
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);

        var (exitCode, _, error) = await InvokeAsync("--test", testPath, "--out", "--fhir-version", "4.0");

        exitCode.ShouldBe(2);
        error.ShouldContain("--out");
    }

    [Fact]
    public async Task GivenDanglingFhirVersionFlag_WhenInvokingActualCommand_ThenReturnsUsageErrorExitCode()
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);
        var outDir = NewOutDir();

        var (exitCode, _, error) = await InvokeAsync("--test", testPath, "--out", outDir, "--fhir-version");

        exitCode.ShouldBe(2);
        error.ShouldContain("--fhir-version");
    }

    [Fact]
    public async Task GivenDanglingFixtureVariantsFlag_WhenInvokingActualCommand_ThenReturnsUsageErrorMentioningOption()
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);
        var outDir = NewOutDir();

        var (exitCode, _, error) = await InvokeAsync(
            "--test", testPath, "--out", outDir, "--fhir-version", "4.0", "--fixture-variants");

        exitCode.ShouldBe(2);
        error.ShouldContain("--fixture-variants requires a positive integer value");
    }

    // ------------------------------------------------------------------------------------------
    // 2. Invalid/unsupported --fhir-version -> 2 with exact expected-values message
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task GivenUnsupportedFhirVersion_WhenInvokingActualCommand_ThenReturnsUsageErrorWithExpectedValuesMessage()
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);
        var outDir = NewOutDir();

        var (exitCode, _, error) = await InvokeAsync("--test", testPath, "--out", outDir, "--fhir-version", "6.0");

        exitCode.ShouldBe(2);
        error.ShouldContain("error: unsupported --fhir-version '6.0'; expected 4.0, 4.3, or 5.0");
    }

    [Theory]
    [InlineData("4.0.1")]
    [InlineData("3.0")]
    [InlineData("STU3")]
    public async Task GivenUnrecognizedFhirVersionVariant_WhenInvokingActualCommand_ThenReturnsUsageErrorExitCode(string version)
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);
        var outDir = NewOutDir();

        var (exitCode, _, _) = await InvokeAsync("--test", testPath, "--out", outDir, "--fhir-version", version);

        exitCode.ShouldBe(2);
    }

    // ------------------------------------------------------------------------------------------
    // 3. Missing test file / existing-file --out -> 2, untouched
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task GivenNonexistentTestFile_WhenInvokingActualCommand_ThenReturnsUsageErrorExitCode()
    {
        var missingPath = Path.Combine(_root, "does-not-exist.json");
        var outDir = NewOutDir();

        var (exitCode, _, error) = await InvokeAsync("--test", missingPath, "--out", outDir, "--fhir-version", "4.0");

        exitCode.ShouldBe(2);
        error.ShouldContain("not found");
        Directory.Exists(outDir).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenOutPathIsExistingFile_WhenInvokingActualCommand_ThenReturnsUsageErrorAndLeavesFileUntouched()
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);
        var outAsFile = Path.Combine(_root, "out-is-a-file");
        await File.WriteAllTextAsync(outAsFile, "sentinel-file-contents");

        var (exitCode, _, error) = await InvokeAsync("--test", testPath, "--out", outAsFile, "--fhir-version", "4.0");

        exitCode.ShouldBe(2);
        error.ShouldContain("already exists as a file");
        File.Exists(outAsFile).ShouldBeTrue();
        (await File.ReadAllTextAsync(outAsFile)).ShouldBe("sentinel-file-contents");
    }

    // ------------------------------------------------------------------------------------------
    // 4. Invalid/nonpositive --fixture-variants -> 2; omitted remains allowed
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task GivenMalformedFixtureVariants_WhenInvokingActualCommand_ThenReturnsUsageErrorExitCode()
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);
        var outDir = NewOutDir();

        var (exitCode, _, error) = await InvokeAsync(
            "--test", testPath, "--out", outDir, "--fhir-version", "4.0", "--fixture-variants", "not-a-number");

        exitCode.ShouldBe(2);
        error.ShouldContain("--fixture-variants must be a positive integer");
    }

    [Fact]
    public async Task GivenZeroFixtureVariants_WhenInvokingActualCommand_ThenReturnsUsageErrorExitCode()
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);
        var outDir = NewOutDir();

        var (exitCode, _, error) = await InvokeAsync(
            "--test", testPath, "--out", outDir, "--fhir-version", "4.0", "--fixture-variants", "0");

        exitCode.ShouldBe(2);
        error.ShouldContain("--fixture-variants must be a positive integer");
    }

    [Fact]
    public async Task GivenNegativeFixtureVariants_WhenCallingRunAsyncDirectly_ThenReturnsUsageErrorExitCode()
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);
        var outDir = NewOutDir();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await CompileLocustCommand.RunAsync(
            testPath, outDir, "4.0", -5, stdout, stderr, CancellationToken.None);

        exitCode.ShouldBe(2);
        stderr.ToString().ShouldContain("--fixture-variants must be a positive integer");
    }

    [Fact]
    public async Task GivenNegativeFixtureVariants_WhenInvokingActualCommand_ThenReturnsUsageErrorExitCode()
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);
        var outDir = NewOutDir();

        var (exitCode, _, error) = await InvokeAsync(
            "--test", testPath, "--out", outDir, "--fhir-version", "4.0", "--fixture-variants", "-5");

        exitCode.ShouldBe(2);
        error.ShouldContain("--fixture-variants must be a positive integer");
    }

    [Fact]
    public async Task GivenOmittedFixtureVariants_WhenInvokingActualCommand_ThenSucceeds()
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);
        var outDir = NewOutDir();

        var (exitCode, _, _) = await InvokeAsync("--test", testPath, "--out", outDir, "--fhir-version", "4.0");

        exitCode.ShouldBe(0);
    }

    // ------------------------------------------------------------------------------------------
    // 5. Invalid JSON/missing required fields -> 1; every parse diagnostic printed
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task GivenTestScriptMissingRequiredName_WhenInvokingActualCommand_ThenReturnsCompilationErrorAndPrintsParseDiagnostic()
    {
        var testPath = await WriteTestScriptAsync("""{"resourceType":"TestScript","status":"active"}""");
        var outDir = NewOutDir();

        var (exitCode, _, error) = await InvokeAsync("--test", testPath, "--out", outDir, "--fhir-version", "4.0");

        exitCode.ShouldBe(1);
        error.ShouldContain("error TESTSCRIPT_PARSE");
        error.ShouldContain($"{testPath}:$.name");
        error.ShouldContain("Required field 'name' is missing");
        Directory.Exists(outDir).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenInvalidJson_WhenInvokingActualCommand_ThenReturnsCompilationErrorAndPrintsParseDiagnostic()
    {
        var testPath = await WriteTestScriptAsync("{ this is not valid json");
        var outDir = NewOutDir();

        var (exitCode, _, error) = await InvokeAsync("--test", testPath, "--out", outDir, "--fhir-version", "4.0");

        exitCode.ShouldBe(1);
        error.ShouldContain("error TESTSCRIPT_PARSE");
        error.ShouldContain("Invalid JSON");
        Directory.Exists(outDir).ShouldBeFalse();
    }

    // ------------------------------------------------------------------------------------------
    // 6. Multiple analyzer errors -> 1; every compiler error code/source printed; sentinel unchanged
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task GivenMultipleUnsupportedOperationFeatures_WhenInvokingActualCommand_ThenReturnsCompilationErrorAndPrintsEveryDiagnostic()
    {
        const string json = """
            {
              "resourceType": "TestScript",
              "name": "MultiError",
              "status": "active",
              "setup": {
                "action": [
                  {
                    "operation": {
                      "type": { "code": "read" },
                      "url": "Patient/1",
                      "destination": 2,
                      "origin": 1,
                      "targetId": "abc"
                    }
                  }
                ]
              }
            }
            """;
        var testPath = await WriteTestScriptAsync(json);
        var outDir = NewOutDir();
        await SeedSentinelDirectoryAsync(outDir);

        var (exitCode, _, error) = await InvokeAsync("--test", testPath, "--out", outDir, "--fhir-version", "4.0");

        exitCode.ShouldBe(1);
        error.ShouldContain("error LOCUST001");
        error.ShouldContain("error LOCUST002");
        error.ShouldContain("error LOCUST003");
        File.Exists(Path.Combine(outDir, "sentinel.txt")).ShouldBeTrue();
    }

    // ------------------------------------------------------------------------------------------
    // 7. Parser warning + successful compile -> warning printed, persisted first; info not printed
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task GivenMissingStatusAndOneSetupOperation_WhenInvokingActualCommand_ThenPrintsWarningNotInfoAndPersistsWarningFirst()
    {
        const string json = """
            {
              "resourceType": "TestScript",
              "name": "WarnAndMetric",
              "setup": {
                "action": [
                  {
                    "operation": {
                      "type": { "code": "read" },
                      "url": "Patient/1"
                    }
                  }
                ]
              }
            }
            """;
        var testPath = await WriteTestScriptAsync(json);
        var outDir = NewOutDir();

        var (exitCode, _, error) = await InvokeAsync("--test", testPath, "--out", outDir, "--fhir-version", "4.0");

        exitCode.ShouldBe(0);
        error.ShouldContain("warning TESTSCRIPT_PARSE");
        error.ShouldContain("Recommended field 'status' is missing");
        error.ShouldNotContain("LOCUST_METRIC");

        var diagnostics = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(outDir, "diagnostics.json")))!.AsArray();
        diagnostics.Count.ShouldBeGreaterThanOrEqualTo(2);
        diagnostics[0]!["code"]!.GetValue<string>().ShouldBe("TESTSCRIPT_PARSE");
        diagnostics[0]!["severity"]!.GetValue<string>().ShouldBe("warning");
        diagnostics.Any(d => d!["code"]!.GetValue<string>() == "LOCUST_METRIC" && d["severity"]!.GetValue<string>() == "info")
            .ShouldBeTrue();
    }

    // ------------------------------------------------------------------------------------------
    // 8. Successful compile -> 0; exactly five flat files; metadata.source basename; sentinel replaced
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task GivenValidMinimalTestScript_WhenInvokingActualCommand_ThenWritesExactlyFiveFlatFilesAndReplacesSentinel()
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);
        var outDir = NewOutDir();
        await SeedSentinelDirectoryAsync(outDir);

        var (exitCode, output, _) = await InvokeAsync("--test", testPath, "--out", outDir, "--fhir-version", "4.0");

        exitCode.ShouldBe(0);
        output.ShouldContain(outDir);

        var entries = Directory.GetFileSystemEntries(outDir);
        entries.Length.ShouldBe(5);
        entries.ShouldAllBe(e => File.Exists(e));

        Path.Combine(outDir, "sentinel.txt").ShouldSatisfyAllConditions(
            p => File.Exists(p).ShouldBeFalse());

        var irDocument = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(outDir, "testscript.ir.json")))!;
        irDocument["metadata"]!["source"]!.GetValue<string>().ShouldBe("test.json");
    }

    // ------------------------------------------------------------------------------------------
    // 9. fhirfakes fixture without --fixture-variants -> 1 with LOCUST007; no artifact/replacement
    // ------------------------------------------------------------------------------------------

    private const string FhirFakesFixtureJson = """
        {
          "resourceType": "TestScript",
          "name": "FakesFixture",
          "status": "active",
          "fixture": [
            {
              "id": "fake-fixture",
              "resource": {
                "extension": [
                  { "url": "http://ignixa.io/testscript/fhirfakes", "valueCode": "Patient" }
                ]
              }
            }
          ]
        }
        """;

    [Fact]
    public async Task GivenFhirFakesFixtureWithoutFixtureVariants_WhenInvokingActualCommand_ThenReturnsCompilationErrorWithLocust007()
    {
        var testPath = await WriteTestScriptAsync(FhirFakesFixtureJson);
        var outDir = NewOutDir();
        await SeedSentinelDirectoryAsync(outDir);

        var (exitCode, _, error) = await InvokeAsync("--test", testPath, "--out", outDir, "--fhir-version", "4.0");

        exitCode.ShouldBe(1);
        error.ShouldContain("error LOCUST007");
        File.Exists(Path.Combine(outDir, "sentinel.txt")).ShouldBeTrue();
    }

    // ------------------------------------------------------------------------------------------
    // 10. fhirfakes with a positive count -> 0 and requested fixture pool count in IR
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task GivenFhirFakesFixtureWithPositiveFixtureVariants_WhenInvokingActualCommand_ThenSucceedsWithRequestedPoolCount()
    {
        var testPath = await WriteTestScriptAsync(FhirFakesFixtureJson);
        var outDir = NewOutDir();

        var (exitCode, _, _) = await InvokeAsync(
            "--test", testPath, "--out", outDir, "--fhir-version", "4.0", "--fixture-variants", "3");

        exitCode.ShouldBe(0);

        var irDocument = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(outDir, "testscript.ir.json")))!;
        var variants = irDocument["fixtures"]![0]!["variants"]!.AsArray();
        variants.Count.ShouldBe(3);
    }

    // ------------------------------------------------------------------------------------------
    // 11. Pre-cancelled token throws OperationCanceledException; output remains unchanged
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task GivenPreCancelledToken_WhenCallingRunAsyncDirectly_ThenThrowsAndLeavesOutputUnchanged()
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);
        var outDir = NewOutDir();
        await SeedSentinelDirectoryAsync(outDir);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        await Should.ThrowAsync<OperationCanceledException>(() => CompileLocustCommand.RunAsync(
            testPath, outDir, "4.0", null, stdout, stderr, cts.Token));

        File.Exists(Path.Combine(outDir, "sentinel.txt")).ShouldBeTrue();
        Directory.GetFileSystemEntries(outDir).Length.ShouldBe(1);
    }

    // ------------------------------------------------------------------------------------------
    // 12. Unexpected internal failure -> 3 (deterministic via an injected artifact-writer failure)
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task GivenArtifactWriterThrows_WhenCallingInjectedWriterRunAsyncDirectly_ThenReturnsUnexpectedErrorExitCodeAndLeavesOutputUnchanged()
    {
        var testPath = await WriteTestScriptAsync(MinimalSuccessJson);
        var outDir = NewOutDir();
        await SeedSentinelDirectoryAsync(outDir);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        static Task ThrowingWriter(LocustIrDocument document, IReadOnlyList<LocustDiagnostic> diagnostics, string outputDirectory, CancellationToken cancellationToken)
            => throw new IOException("simulated writer failure");

        var exitCode = await CompileLocustCommand.RunAsync(
            testPath, outDir, "4.0", null, stdout, stderr, ThrowingWriter, CancellationToken.None);

        exitCode.ShouldBe(3);
        stderr.ToString().ShouldContain("IOException");
        stderr.ToString().ShouldContain("simulated writer failure");
        File.Exists(Path.Combine(outDir, "sentinel.txt")).ShouldBeTrue();
        Directory.GetFileSystemEntries(outDir).Length.ShouldBe(1);
    }
}
