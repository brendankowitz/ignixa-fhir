using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.ConformanceMatrix.Cli.Reporting;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.FhirFakes;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Parsing;
using Ignixa.TestScript.Reporting;

namespace Ignixa.ConformanceMatrix.Cli.Commands;

internal static class RunCommand
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    // TestReport.tester is "the name of the tester producing this report", which is this tool — not
    // the server under test. The server is named by participant[server].display.
    private const string ToolName = "Ignixa.ConformanceMatrix.Cli";

    // Distinguishes "nothing ran because the invocation was wrong" from "the suite ran and tests
    // failed" (ClassifyExitCode's 1), which a CI job otherwise cannot tell apart.
    internal const int UsageErrorExitCode = ExitCodes.UsageError;
    internal const int InternalErrorExitCode = ExitCodes.InternalError;

    public static Command Build()
    {
        var command = new Command("run", "Run a TestScript suite against a FHIR server and write a per-impl report");

        var serverOption = new Option<string>("--server") { Description = "Base URL of the FHIR server", Required = true };
        var testsOption = new Option<string>("--tests") { Description = "Folder containing TestScript .json files", Required = true };
        var implOption = new Option<string>("--impl") { Description = "Implementation name (column label in the matrix)", Required = true };
        var outOption = new Option<string>("--out") { Description = "Output path for the report file; --format selects its shape", Required = true };
        var fhirVersionOption = new Option<string?>("--fhir-version")
        {
            Description = "FHIR version to test against (e.g. '4.0', '4.3', '5.0'). Sets fhirVersion on the Accept header and skips tests not tagged for this version. Omit to run all tests against any server."
        };
        var authHeaderOption = new Option<string?>("--auth-header")
        {
            Description = "Authentication header value to apply to every request (for example 'Bearer <token>' or 'Authorization: Bearer <token>')"
        };
        var formatOption = new Option<ReportFormat>("--format")
        {
            Description = "Shape of the --out file: 'fhir' (a Bundle of FHIR TestReport resources, the default) or 'json' (this tool's native per-impl report, which is what 'merge' consumes)",
            DefaultValueFactory = _ => ReportFormat.Fhir
        };

        command.Options.Add(serverOption);
        command.Options.Add(testsOption);
        command.Options.Add(implOption);
        command.Options.Add(outOption);
        command.Options.Add(fhirVersionOption);
        command.Options.Add(authHeaderOption);
        command.Options.Add(formatOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var server = parseResult.GetValue(serverOption)!;
            var tests = parseResult.GetValue(testsOption)!;
            var impl = parseResult.GetValue(implOption)!;
            var outPath = parseResult.GetValue(outOption)!;
            var fhirVersion = parseResult.GetValue(fhirVersionOption);
            var authHeader = parseResult.GetValue(authHeaderOption);
            var format = parseResult.GetValue(formatOption);
            return RunAsync(server, tests, impl, outPath, fhirVersion, authHeader, format, cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// Runs the suite and writes the report, returning the process exit code: 0 when everything
    /// passed or was skipped, 1 when tests failed or errored, 2 when the invocation was unusable so
    /// nothing ran, and 3 on an unexpected internal failure.
    /// </summary>
    internal static async Task<int> RunAsync(string server, string testsPath, string impl, string outPath, string? fhirVersion, string? authHeader, ReportFormat format, CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(testsPath))
            {
                Console.Error.WriteLine($"error: --tests directory not found: {testsPath}");
                return UsageErrorExitCode;
            }

            if (!Uri.TryCreate(server, UriKind.Absolute, out _))
            {
                Console.Error.WriteLine($"error: --server is not a valid absolute URI: {server}");
                return UsageErrorExitCode;
            }

            // Before the suite runs, not at write time: a typo in --out that only surfaces after a
            // full run against a live server discards every result it just spent minutes collecting.
            if (PrepareOutputDirectory(outPath) is { } outError)
            {
                Console.Error.WriteLine($"error: {outError}");
                return UsageErrorExitCode;
            }

            var startedAt = DateTimeOffset.UtcNow;

            var files = Directory.EnumerateFiles(testsPath, "*.json", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                Console.Error.WriteLine($"error: no .json files found in {testsPath} — no tests to run");
                return UsageErrorExitCode;
            }

            var schema = new R4CoreSchemaProvider();
            using var httpClient = new HttpClient { BaseAddress = new Uri(server.TrimEnd('/') + '/') };
            if (AuthHeader.Apply(httpClient, authHeader) is { } authError)
            {
                Console.Error.WriteLine($"error: {authError}");
                return UsageErrorExitCode;
            }

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
            var bundleResources = new List<JsonObject>();

            // Only the FHIR bundle carries these; the native json report already records every
            // failure as an "error" result, so the resource is not built at all under --format json.
            void AddBundleResource(Func<JsonObject> resource)
            {
                if (format == ReportFormat.Fhir)
                    bundleResources.Add(resource());
            }

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
                    AddBundleResource(() => BuildParseErrorOutcome(relFile, $"{ex.GetType().Name}: {ex.Message}"));
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
                    AddBundleResource(() => BuildParseErrorOutcome(relFile, messages));
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
                    AddBundleResource(() => TestReportResourceGenerator.Generate(report, new TestReportContext
                    {
                        Tester = ToolName,
                        ServerUri = httpClient.BaseAddress,
                        ServerDisplay = impl,
                        TestScriptDisplay = relFile
                    }));

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
                    AddBundleResource(() => BuildEvaluatorErrorOutcome(relFile, ex));
                }
            }

            var duration = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
            var payload = BuildPayload(format, impl, startedAt, duration, allResults, bundleResources);

            await File.WriteAllTextAsync(outPath, payload, cancellationToken);

            Console.WriteLine($"\n{impl}: {FormatOutcomeSummary(allResults)} ({duration}ms) -> {outPath}");
            return ClassifyExitCode(allResults);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The full exception, not just Message: this catch only sees bugs and unhandled
            // transport failures, where the stack and the inner chain are the actionable part —
            // a bare "NullReferenceException: Object reference not set" is not, and an
            // HttpRequestException's message hides the SocketException that explains it.
            Console.Error.WriteLine($"error: unexpected failure: {ex}");
            return InternalErrorExitCode;
        }
    }

    /// <summary>
    /// Records a script the runner could not parse, so a failure that produces no TestReport still
    /// appears in the FHIR bundle rather than only in the console scrollback.
    /// </summary>
    internal static JsonObject BuildParseErrorOutcome(string file, string detail) =>
        OperationOutcomeResourceGenerator.Generate(
            OperationOutcomeResourceGenerator.StructureIssueCode, $"{file}: {detail}");

    /// <summary>Records a script whose execution threw rather than producing a report.</summary>
    internal static JsonObject BuildEvaluatorErrorOutcome(string file, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return OperationOutcomeResourceGenerator.Generate(
            OperationOutcomeResourceGenerator.ExceptionIssueCode,
            $"{file}: {error.GetType().Name}: {error.Message}");
    }

    internal static string FormatOutcomeSummary(IReadOnlyList<ImplReportResult> results)
    {
        var pass = results.Count(r => r.Status == "pass");
        var fail = results.Count(r => r.Status == "fail");
        var skipped = results.Count(r => r.Status == "skipped");
        var error = results.Count(r => r.Status == "error");
        return $"{pass} passed, {fail} failed, {skipped} skipped, {error} error(s)";
    }

    internal static string BuildPayload(
        ReportFormat format,
        string impl,
        DateTimeOffset startedAt,
        long durationMs,
        IReadOnlyList<ImplReportResult> results,
        IReadOnlyList<JsonObject> bundleResources) => format switch
        {
            ReportFormat.Json => SerializeImplReport(impl, startedAt, durationMs, results),
            ReportFormat.Fhir => BuildFhirBundle(bundleResources, startedAt).ToJsonString(SerializerOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unhandled report format")
        };

    /// <summary>
    /// Wraps the run's resources — a TestReport per executed script, an OperationOutcome per script
    /// that could not be — in a <c>collection</c> Bundle.
    /// </summary>
    internal static JsonObject BuildFhirBundle(IReadOnlyList<JsonObject> resources, DateTimeOffset timestamp)
    {
        var payload = new JsonObject
        {
            ["resourceType"] = "Bundle",
            ["type"] = "collection",
            ["timestamp"] = timestamp.ToString("o")
        };

        if (resources.Count == 0)
            return payload;

        var entries = new JsonArray();
        foreach (var resource in resources)
        {
            // Bundle.entry.fullUrl must be absolute and agree with Resource.id; these resources
            // are never persisted and carry no id, so urn:uuid is the form that fits — and it
            // cannot collide the way a slugified file path can.
            entries.Add(new JsonObject
            {
                ["fullUrl"] = $"urn:uuid:{Guid.NewGuid()}",
                ["resource"] = resource
            });
        }

        // FHIR JSON prohibits empty arrays, so entry is omitted above rather than emitted as [].
        payload["entry"] = entries;
        return payload;
    }

    private static string SerializeImplReport(string impl, DateTimeOffset startedAt, long durationMs, IReadOnlyList<ImplReportResult> results) =>
        JsonSerializer.Serialize(
            new ImplReport { Impl = impl, StartedAt = startedAt, DurationMs = durationMs, Results = results },
            SerializerOptions);

    /// <summary>
    /// Creates the directory <paramref name="path"/> will be written to, returning an error message
    /// when the path is unusable and <c>null</c> on success.
    /// </summary>
    /// <remarks>
    /// Path.GetDirectoryName returns null for a root path (e.g. "C:\"), where there is nothing to
    /// create; Directory.CreateDirectory(null) would throw instead. Path.GetFullPath rejects invalid
    /// path characters by throwing, which is a usage error to report rather than an internal fault.
    /// </remarks>
    internal static string? PrepareOutputDirectory(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
            or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return $"--out path is unusable: {path} ({ex.GetType().Name}: {ex.Message})";
        }
    }

    internal static int ClassifyExitCode(IReadOnlyList<ImplReportResult> results)
        => results.Any(r => MatrixBuilder.IsFail(r.Status)) ? 1 : 0;

    /// <summary>
    /// Applies <paramref name="authHeader"/> to <paramref name="httpClient"/>, returning an error
    /// message when it cannot be applied and <c>null</c> on success.
    /// </summary>
    /// <remarks>
    /// Every failure here must stop the run. Applying no header would exercise the whole suite
    /// unauthenticated and report each 401 as a legitimate test failure, which is indistinguishable
    /// from a broken server. Note the null check rather than IsNullOrWhiteSpace: an omitted flag is
    /// null and means "no auth", while an explicit empty value is a mistake worth reporting — most
    /// often an environment variable that expanded to nothing.
    /// </remarks>
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
