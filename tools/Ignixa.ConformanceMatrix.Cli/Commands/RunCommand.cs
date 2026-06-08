using System.CommandLine;
using System.Text.Json;
using Ignixa.ConformanceMatrix.Cli.Reporting;
using Ignixa.Specification.Generated;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.FhirFakes;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Parsing;

namespace Ignixa.ConformanceMatrix.Cli.Commands;

internal static class RunCommand
{
    public static Command Build()
    {
        var command = new Command("run", "Run a TestScript suite against a FHIR server and write a per-impl report");

        var serverOption = new Option<string>("--server") { Description = "Base URL of the FHIR server", Required = true };
        var testsOption = new Option<string>("--tests") { Description = "Folder containing TestScript .json files", Required = true };
        var implOption = new Option<string>("--impl") { Description = "Implementation name (column label in the matrix)", Required = true };
        var outOption = new Option<string>("--out") { Description = "Output path for the per-impl report JSON", Required = true };
        var fhirVersionOption = new Option<string?>("--fhir-version")
        {
            Description = "FHIR version to test against (e.g. '4.0', '4.3', '5.0'). Sets fhirVersion on Content-Type/Accept headers and skips tests not tagged for this version. Omit to run all tests against any server."
        };

        command.Options.Add(serverOption);
        command.Options.Add(testsOption);
        command.Options.Add(implOption);
        command.Options.Add(outOption);
        command.Options.Add(fhirVersionOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var server = parseResult.GetValue(serverOption)!;
            var tests = parseResult.GetValue(testsOption)!;
            var impl = parseResult.GetValue(implOption)!;
            var outPath = parseResult.GetValue(outOption)!;
            var fhirVersion = parseResult.GetValue(fhirVersionOption);
            return RunAsync(server, tests, impl, outPath, fhirVersion, cancellationToken);
        });

        return command;
    }

    private static async Task<int> RunAsync(string server, string testsPath, string impl, string outPath, string? fhirVersion, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var files = Directory.EnumerateFiles(testsPath, "*.json", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var schema = new R4CoreSchemaProvider();
        using var httpClient = new HttpClient { BaseAddress = new Uri(server.TrimEnd('/') + '/') };
        if (fhirVersion is not null)
        {
            var mediaType = $"application/fhir+json; fhirVersion={fhirVersion}";
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(
                System.Net.Http.Headers.MediaTypeWithQualityHeaderValue.Parse(mediaType));
        }
        var provider = new HttpTestRequestProvider(httpClient);
        var fixtureProvider = new CompositeFixtureProvider(
        [
            new FhirFakesFixtureProvider(),
            new InlineFixtureProvider()
        ]);
        var evaluator = new TestScriptEvaluator(provider, fixtureProvider, schema);

        var allResults = new List<ImplReportResult>();
        foreach (var file in files)
        {
            var relFile = Path.GetRelativePath(testsPath, file).Replace('\\', '/');
            Console.WriteLine($"  running {relFile}...");

            var parseResult = TestScriptParser.ParseFile(file);
            if (!parseResult.IsSuccess)
            {
                var messages = string.Join("; ", parseResult.Errors.Select(e => e.Message));
                Console.Error.WriteLine($"  PARSE ERROR: {messages}");
                allResults.Add(new ImplReportResult
                {
                    Id = relFile,
                    File = relFile,
                    Status = "fail",
                    DurationMs = 0,
                    Error = new CellError { Assertion = "Parse error", Received = messages }
                });
                continue;
            }

            try
            {
                var report = await evaluator.ExecuteAsync(parseResult.Value!, cancellationToken, fhirVersion: fhirVersion);
                var mapped = ReportMapper.Map(report, relFile);
                allResults.AddRange(mapped);

                var pass = mapped.Count(r => r.Status == "pass");
                var fail = mapped.Count(r => r.Status != "pass" && r.Status != "skipped");
                Console.WriteLine($"    {pass} passed, {fail} failed");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  ERROR evaluating {relFile}: {ex.Message}");
                allResults.Add(new ImplReportResult
                {
                    Id = relFile,
                    File = relFile,
                    Status = "error",
                    DurationMs = 0,
                    Error = new CellError { Assertion = "Evaluator error", Received = ex.Message }
                });
            }
        }

        var duration = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        var implReport = new ImplReport
        {
            Impl = impl,
            StartedAt = startedAt.ToString("O"),
            DurationMs = duration,
            Results = allResults
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        var json = JsonSerializer.Serialize(implReport, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outPath, json, cancellationToken);

        var totalPass = allResults.Count(r => r.Status == "pass");
        var totalFail = allResults.Count(r => r.Status == "fail");
        Console.WriteLine($"\n{impl}: {totalPass} passed, {totalFail} failed ({duration}ms) -> {outPath}");
        return totalFail > 0 ? 1 : 0;
    }
}
