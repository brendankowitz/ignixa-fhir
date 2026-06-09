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
            Description = "FHIR version to test against (e.g. '4.0', '4.3', '5.0'). Sets fhirVersion on the Accept header and skips tests not tagged for this version. Omit to run all tests against any server."
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
        try
        {
            if (!Directory.Exists(testsPath))
            {
                Console.Error.WriteLine($"error: --tests directory not found: {testsPath}");
                return 1;
            }

            if (!Uri.TryCreate(server, UriKind.Absolute, out _))
            {
                Console.Error.WriteLine($"error: --server is not a valid absolute URI: {server}");
                return 1;
            }

            var startedAt = DateTimeOffset.UtcNow;

            var files = Directory.EnumerateFiles(testsPath, "*.json", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                Console.Error.WriteLine($"error: no .json files found in {testsPath} — no tests to run");
                return 1;
            }

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

                Ignixa.TestScript.Parsing.ParseResult<Ignixa.TestScript.Model.TestScriptDefinition> parseResult;
                try
                {
                    parseResult = TestScriptParser.ParseFile(file);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  PARSE ERROR {relFile}: {ex.GetType().Name}: {ex.Message}");
                    allResults.Add(new ImplReportResult
                    {
                        Id = relFile,
                        File = relFile,
                        Status = "error",
                        DurationMs = 0,
                        Error = new CellError { Assertion = "Parse exception", Received = ex.Message }
                    });
                    continue;
                }

                if (!parseResult.IsSuccess)
                {
                    var messages = string.Join("; ", parseResult.Errors.Select(e => e.Message));
                    Console.Error.WriteLine($"  PARSE ERROR {relFile}: {messages}");
                    allResults.Add(new ImplReportResult
                    {
                        Id = relFile,
                        File = relFile,
                        Status = "error",
                        DurationMs = 0,
                        Error = new CellError { Assertion = "Parse error", Received = messages }
                    });
                    continue;
                }

                if (parseResult.Errors.Count > 0)
                {
                    foreach (var warning in parseResult.Errors)
                        Console.Error.WriteLine($"  PARSE WARNING {relFile}: {warning.Message}");
                }

                try
                {
                    var report = await evaluator.ExecuteAsync(parseResult.Value!, cancellationToken, fhirVersion: fhirVersion);
                    var mapped = ReportMapper.Map(report, relFile);
                    allResults.AddRange(mapped);

                    var pass = mapped.Count(r => r.Status == "pass");
                    var fail = mapped.Count(r => MatrixBuilder.IsFail(r.Status));
                    Console.WriteLine($"    {pass} passed, {fail} failed");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  ERROR evaluating {relFile}: {ex.GetType().Name}: {ex.Message}");
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
                StartedAt = startedAt,
                DurationMs = duration,
                Results = allResults
            };

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            var json = JsonSerializer.Serialize(implReport, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outPath, json, cancellationToken);

            var totalPass = allResults.Count(r => r.Status == "pass");
            var totalFail = allResults.Count(r => MatrixBuilder.IsFail(r.Status));
            var totalError = allResults.Count(r => r.Status == "error");
            Console.WriteLine($"\n{impl}: {totalPass} passed, {totalFail} failed, {totalError} error(s) ({duration}ms) -> {outPath}");
            return ClassifyExitCode(allResults);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    internal static int ClassifyExitCode(IReadOnlyList<ImplReportResult> results)
        => results.Any(r => MatrixBuilder.IsFail(r.Status)) ? 1 : 0;
}
