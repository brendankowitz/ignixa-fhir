using System.CommandLine;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.ConformanceMatrix.Cli.Reporting;
using Ignixa.Models;
using Ignixa.Serialization;
using Ignixa.TestScript.Reporting;
using Ignixa.Serialization.SourceNodes;
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
        var authHeaderOption = new Option<string?>("--auth-header")
        {
            Description = "Authentication header value to apply to every request (for example 'Bearer <token>' or 'Authorization: Bearer <token>')"
        };
        var testReportOption = new Option<string?>("--test-report")
        {
            Description = "Optional path to write FHIR TestReport output as a JSON resource or Bundle"
        };

        command.Options.Add(serverOption);
        command.Options.Add(testsOption);
        command.Options.Add(implOption);
        command.Options.Add(outOption);
        command.Options.Add(fhirVersionOption);
        command.Options.Add(authHeaderOption);
        command.Options.Add(testReportOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var server = parseResult.GetValue(serverOption)!;
            var tests = parseResult.GetValue(testsOption)!;
            var impl = parseResult.GetValue(implOption)!;
            var outPath = parseResult.GetValue(outOption)!;
            var fhirVersion = parseResult.GetValue(fhirVersionOption);
            var authHeader = parseResult.GetValue(authHeaderOption);
            var testReportPath = parseResult.GetValue(testReportOption);
            return RunAsync(server, tests, impl, outPath, fhirVersion, authHeader, testReportPath, cancellationToken);
        });

        return command;
    }

    private static async Task<int> RunAsync(string server, string testsPath, string impl, string outPath, string? fhirVersion, string? authHeader, string? testReportPath, CancellationToken cancellationToken)
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
            ApplyAuthHeader(httpClient, authHeader);
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

            var capabilityStatement = await FetchCapabilityStatementAsync(httpClient, cancellationToken);

            var allResults = new List<ImplReportResult>();
            var testReports = new List<JsonObject>();
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
                    var report = await evaluator.ExecuteAsync(parseResult.Value!, cancellationToken,
                        fhirVersion: fhirVersion, capabilityStatement: capabilityStatement);
                    if (!string.IsNullOrWhiteSpace(testReportPath))
                        testReports.Add(TestReportResourceGenerator.Generate(report));

                    var mapped = ReportMapper.Map(report, relFile);
                    allResults.AddRange(mapped);

                    Console.WriteLine($"    {FormatOutcomeSummary(mapped)}");
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

            if (!string.IsNullOrWhiteSpace(testReportPath))
                await WriteTestReportAsync(testReportPath, testReports, cancellationToken);

            Console.WriteLine($"\n{impl}: {FormatOutcomeSummary(allResults)} ({duration}ms) -> {outPath}");
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

    internal static string FormatOutcomeSummary(IReadOnlyList<ImplReportResult> results)
    {
        var pass = results.Count(r => r.Status == "pass");
        var fail = results.Count(r => r.Status == "fail");
        var skipped = results.Count(r => r.Status == "skipped");
        var error = results.Count(r => r.Status == "error");
        return $"{pass} passed, {fail} failed, {skipped} skipped, {error} error(s)";
    }

    internal static (string Name, string Value) ParseAuthHeader(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length == 0)
            return ("Authorization", string.Empty);

        if (trimmed.Contains(':')
            && !trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("Digest ", StringComparison.OrdinalIgnoreCase))
        {
            var separatorIndex = trimmed.IndexOf(':');
            return (trimmed[..separatorIndex].Trim(), trimmed[(separatorIndex + 1)..].Trim());
        }

        return ("Authorization", trimmed);
    }

    internal static JsonObject BuildTestReportPayload(IReadOnlyList<JsonObject> reports)
    {
        if (reports.Count == 1)
            return reports[0];

        var bundle = new Bundle();
        ((IMutableJsonNode)bundle).MutableNode["type"] = "collection";

        foreach (var report in reports)
        {
            bundle.Entry.Add(new Ignixa.Models.BundleEntry
            {
                Resource = new ResourceJsonNode(report)
            });
        }

        return ((IMutableJsonNode)bundle).MutableNode;
    }

    internal static async Task WriteTestReportAsync(string path, IReadOnlyList<JsonObject> reports, CancellationToken cancellationToken)
    {
        var payload = BuildTestReportPayload(reports);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var json = payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    internal static int ClassifyExitCode(IReadOnlyList<ImplReportResult> results)
        => results.Any(r => MatrixBuilder.IsFail(r.Status)) ? 1 : 0;

    private static void ApplyAuthHeader(HttpClient httpClient, string? authHeader)
    {
        if (string.IsNullOrWhiteSpace(authHeader))
            return;

        var (name, value) = ParseAuthHeader(authHeader);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
            return;

        if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
        {
            if (AuthenticationHeaderValue.TryParse(value, out var parsed))
                httpClient.DefaultRequestHeaders.Authorization = parsed;
            else
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
            return;
        }

        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
    }

    /// <summary>
    /// Fetches the target server's CapabilityStatement from <c>/metadata</c> once per run, so it
    /// can be passed to <see cref="TestScriptEvaluator.ExecuteAsync"/> for <c>requiresCapability</c>
    /// gating. Any failure (network error, non-success status, unparseable body) is treated as
    /// "no CapabilityStatement available" — capability gating fails open in that case, matching
    /// the evaluator's own policy — but is reported so it isn't silently swallowed.
    /// </summary>
    internal static async Task<ResourceJsonNode?> FetchCapabilityStatementAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(new Uri("metadata", UriKind.Relative), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"warning: could not fetch /metadata (HTTP {(int)response.StatusCode}); requiresCapability gating will fail open for this run");
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSourceNodeFactory.Parse(body);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"warning: could not fetch /metadata ({ex.GetType().Name}: {ex.Message}); requiresCapability gating will fail open for this run");
            return null;
        }
    }
}
