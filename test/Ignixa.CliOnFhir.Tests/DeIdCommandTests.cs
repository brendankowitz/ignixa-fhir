// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Commands;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DeIdFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>The verb writes to the process-wide console, so these tests must not run beside others that do.</summary>
[Collection("Console")]
public class DeIdCommandTests
{
    [Fact]
    public async Task GivenAConfigurationFileAndAnOutputDirectory_WhenRunOnTheCli_ThenTheEnvelopeIsPrintedAndTheFilesWritten()
    {
        using TestFolder root = Root().Write("config.json", Config);

        (int exitCode, string output, _) = await RunCliAsync("deid", Data(root), "--config", Path.Combine(root.Path, "config.json"), "--out", Out(root), "--patient", "p1", "--no-cache");

        exitCode.ShouldBe(0);
        JsonNode envelope = JsonNode.Parse(output)!;
        ((int)envelope["facts"]!["written"]!).ShouldBe(2);
        ((string)envelope["facts"]!["scope"]!["kind"]!).ShouldBe("patient");
        File.Exists(Path.Combine(Out(root), "Patient.ndjson")).ShouldBeTrue();
        envelope["next"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenNoConfiguration_WhenRunOnTheCli_ThenTheIssueIsPrintedAndExitCodeIsNonZero()
    {
        using TestFolder root = Root();

        (int exitCode, string output, _) = await RunCliAsync("deid", Data(root), "--out", Out(root), "--no-cache");

        exitCode.ShouldNotBe(0);
        ((string)JsonNode.Parse(output)!["issues"]![0]!["code"]!).ShouldBe("deid-config-required");
    }

    // The keys, the rules' paths and the resources' values must not reach stdout or stderr, whether the run succeeds, fails on a
    // resource, or refuses the configuration.
    [Fact]
    public async Task GivenAConfigurationWithKeys_WhenRunsSucceedFailOrAreRefused_ThenNoKeyPathOrValueReachesStdoutOrStderr()
    {
        const string rulePath = "Patient.name.single()";
        string withKeys = Config.Replace("\"Patient.name\"", $"\"{rulePath}\"", StringComparison.Ordinal);
        string badEncrypt = $$$"""{"fhirPathRules":[{"path":"Patient.gender","method":"encrypt"}],"parameters":{"encryptKey":"{{{HashKey}}}","cryptoHashKey":"{{{DateKey}}}"}}""";
        string badJson = withKeys + " trailing";
        using TestFolder root = Root().Write("keys.json", withKeys).Write("bad-encrypt.json", badEncrypt).Write("bad-json.json", badJson);

        List<(int ExitCode, string Output, string Errors)> runs = [];
        foreach (string config in new[] { "keys.json", "bad-encrypt.json", "bad-json.json" })
        {
            runs.Add(await RunCliAsync("deid", Data(root), "--config", Path.Combine(root.Path, config), "--out", Path.Combine(root.Path, "out-" + config), "--no-cache"));
        }

        foreach (string inline in new[] { "\uFEFF" + badJson, $$$"""[{"cryptoHashKey":"{{{HashKey}}}"}]""", $"\"cryptoHashKey\":\"{HashKey}\",\"dateShiftKey\":\"{DateKey}\"", $"{HashKey}-{DateKey}" })
        {
            runs.Add(await RunCliAsync("deid", Data(root), "--config", inline, "--out", Path.Combine(root.Path, "out-inline"), "--no-cache"));
        }

        runs.Select(run => run.ExitCode).ShouldBe([0, 1, 1, 1, 1, 1, 1]);
        JsonNode.Parse(runs[0].Output)!["issues"]!.AsArray().ShouldContain(issue => (string)issue!["code"]! == "deid-resource-incomplete");
        foreach ((_, string output, string errors) in runs)
        {
            foreach (string secret in new[] { HashKey, DateKey, rulePath, Mrn, "Smith", "Jones", "1975-03-04" })
            {
                output.ShouldNotContain(secret);
                errors.ShouldNotContain(secret);
            }
        }
    }

    private static async Task<(int ExitCode, string Output, string Errors)> RunCliAsync(params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(DeIdCommand.Create());
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Console.SetOut(output);
        Console.SetError(errors);
        try
        {
            int exitCode = await root.Parse(args).InvokeAsync();
            return (exitCode, output.ToString(), errors.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
