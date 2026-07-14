using System.CommandLine;
using System.Net.Http.Headers;
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

    private static async Task<int> RunAsync(string server, string testsPath, string impl, string outPath, string? fhirVersion, string? authHeader, ReportFormat format, CancellationToken cancellationToken)
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
            if (ApplyAuthHeader(httpClient, authHeader) is { } authError)
            {
                Console.Error.WriteLine($"error: {authError}");
                return 1;
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
                    if (format == ReportFormat.Fhir)
                    {
                        testReports.Add(TestReportResourceGenerator.Generate(report, new TestReportContext
                        {
                            Tester = impl,
                            ServerUri = server,
                            TestScriptDisplay = relFile
                        }));
                    }

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
            var payload = BuildPayload(format, impl, startedAt, duration, allResults, testReports);

            EnsureDirectoryExists(outPath);
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

    // An HTTP header name cannot contain whitespace, so text before the first colon that has none
    // is a header name and anything else is a bare credential for Authorization. This holds for
    // any scheme — Negotiate, NTLM, AWS4-HMAC-SHA256 — without enumerating them.
    internal static (string Name, string Value) ParseAuthHeader(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length == 0)
            return ("Authorization", string.Empty);

        var separatorIndex = trimmed.IndexOf(':');
        if (separatorIndex > 0)
        {
            var name = trimmed[..separatorIndex].Trim();
            if (name.Length > 0 && !name.Any(char.IsWhiteSpace))
                return (name, trimmed[(separatorIndex + 1)..].Trim());
        }

        return ("Authorization", trimmed);
    }

    internal static string BuildPayload(
        ReportFormat format,
        string impl,
        DateTimeOffset startedAt,
        long durationMs,
        IReadOnlyList<ImplReportResult> results,
        IReadOnlyList<JsonObject> testReports) => format switch
        {
            ReportFormat.Json => SerializeImplReport(impl, startedAt, durationMs, results),
            ReportFormat.Fhir => BuildTestReportPayload(testReports, startedAt).ToJsonString(SerializerOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unhandled report format")
        };

    internal static JsonObject BuildTestReportPayload(IReadOnlyList<JsonObject> reports, DateTimeOffset timestamp)
    {
        var payload = new JsonObject
        {
            ["resourceType"] = "Bundle",
            ["type"] = "collection",
            ["timestamp"] = timestamp.ToString("o")
        };

        if (reports.Count == 0)
            return payload;

        var entries = new JsonArray();
        foreach (var report in reports)
        {
            // Bundle.entry.fullUrl must be absolute and agree with Resource.id; these TestReports
            // are never persisted and carry no id, so urn:uuid is the form that fits — and it
            // cannot collide the way a slugified file path can.
            entries.Add(new JsonObject
            {
                ["fullUrl"] = $"urn:uuid:{Guid.NewGuid()}",
                ["resource"] = report
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

    // Path.GetDirectoryName returns null for a root path (e.g. "C:\"), where there is nothing to
    // create; Directory.CreateDirectory(null) would throw instead.
    private static void EnsureDirectoryExists(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
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
    internal static string? ApplyAuthHeader(HttpClient httpClient, string? authHeader)
    {
        if (authHeader is null)
            return null;

        var (name, value) = ParseAuthHeader(authHeader);

        if (string.IsNullOrWhiteSpace(value))
            return $"--auth-header '{authHeader}' resolves to no header value; expected 'Bearer <token>' or 'Header-Name: <value>'. If an environment variable expands to empty, omit the flag instead of passing a blank value.";

        if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
            && AuthenticationHeaderValue.TryParse(value, out var parsed))
        {
            httpClient.DefaultRequestHeaders.Authorization = parsed;
            return null;
        }

        // TryAddWithoutValidation returns false (it does not throw) when the name is not a valid
        // HTTP token, which would otherwise drop the credential without a trace.
        if (!httpClient.DefaultRequestHeaders.TryAddWithoutValidation(name, value))
            return $"--auth-header name '{name}' is not a valid HTTP header name.";

        return null;
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
