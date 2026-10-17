// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Commands;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Output;
using Ignixa.CliOnFhir.Views;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DeIdFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// One call of the FHIRPath evaluator cannot be interrupted, so the CLI ends a run that overruns its timeout by more than the grace.
/// The watchdog writes to the process-wide console, so these tests must not run beside others that do.
/// </summary>
[Collection("Console")]
public class WatchdogTests
{
    [Fact]
    public async Task GivenARunThatNeverReturns_WhenItsTimeoutAndTheGracePass_ThenTheTimeoutErrorIsPrintedWithTheOpenFilesAndTheProcessIsEnded()
    {
        using TestFolder root = Root();
        OutputTarget target = OutputTarget.ForFile(Data(root), Path.Combine(root.Path, "out", "rows.csv"), OutputTarget.Csv, overwrite: false);
        OutputFile open = target.Begin([]);
        int? exited = null;
        var never = new TaskCompletionSource<ToolEnvelope<ViewFacts?>>();

        (int code, string output) = await CaptureAsync(() => Watchdog.RunAsync<ViewFacts>("view-timeout", 1, 30, 3600, _ => never.Task, pretty: false, TimeSpan.FromMilliseconds(50), code => exited = code, CancellationToken.None));
        open.Discard();

        code.ShouldBe(1);
        exited.ShouldBe(1);
        JsonNode envelope = JsonNode.Parse(output)!;
        envelope["facts"].ShouldBeNull();
        JsonArray issues = envelope["issues"]!.AsArray();
        ((string)issues[0]!["code"]!).ShouldBe("view-timeout");
        issues.ShouldContain(issue => (string)issue!["code"]! == "out-temp-left" && ((string)issue["diagnostics"]!).Contains(open.TempPath));
    }

    [Fact]
    public async Task GivenARunThatReturnsInTime_WhenWatched_ThenItsEnvelopeIsPrintedAndTheProcessGoesOn()
    {
        using TestFolder root = Root();
        int? exited = null;

        (int code, string output) = await CaptureAsync(() => Watchdog.RunAsync<ViewFacts>(
            "view-timeout", 5, 30, 3600, token => ViewFixture.RunInAsync(Data(root), new ViewRequest(ViewFixture.PatientView)), pretty: false, TimeSpan.FromMilliseconds(50), code => exited = code, CancellationToken.None));

        code.ShouldBe(0);
        exited.ShouldBeNull();
        ((int)JsonNode.Parse(output)!["facts"]!["total"]!).ShouldBe(2);
    }

    // The repro of the review, below the iterating limit but slow: a view whose one column takes far longer than its timeout ends the
    // CLI process within the timeout and the grace.
    [Fact]
    public async Task GivenAViewColumnThatOutrunsItsTimeout_WhenRunByTheCli_ThenTheProcessEndsWithinTheGraceWithATimeoutError()
    {
        using TestFolder root = Root();
        string union = "(" + string.Join('|', Enumerable.Range(1, 299)) + ")";
        string view = JsonSerializer.Serialize(new JsonObject
        {
            ["resourceType"] = "ViewDefinition",
            ["resource"] = "Patient",
            ["select"] = new JsonArray(new JsonObject { ["column"] = new JsonArray(new JsonObject { ["name"] = "n", ["path"] = $"{union}.select({union}.select({union}.count()).count()).count()" }) }),
        });
        root.Write("slow-view.json", view);
        var info = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in new[] { Path.Combine(AppContext.BaseDirectory, "cli-on-fhir.dll"), "view", Data(root), Path.Combine(root.Path, "slow-view.json"), "--timeout", "1", "--no-cache" })
        {
            info.ArgumentList.Add(argument);
        }

        var clock = Stopwatch.StartNew();
        using Process process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        bool ended = true;
        using (var limit = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
        {
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                ended = false;
                process.Kill(entireProcessTree: true);
            }
        }

        ended.ShouldBeTrue("the CLI did not end within a minute");
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1) + Watchdog.Grace + TimeSpan.FromSeconds(15));
        process.ExitCode.ShouldBe(1);
        ((string)JsonNode.Parse(await output)!["issues"]![0]!["code"]!).ShouldBe("view-timeout", await errors);
    }

    private static async Task<(int Code, string Output)> CaptureAsync(Func<Task<int>> run)
    {
        TextWriter original = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            int code = await run();
            return (code, captured.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
