using Ignixa.Abstractions;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.FhirFakes;
using Ignixa.TestScript.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Ignixa.ConformanceMatrix.Cli.Serving;

/// <summary>
/// Builds the Kestrel-hosted runner: <c>/healthz</c>, <c>/testscripts</c>, and <c>/run</c> over a
/// parse-once <see cref="TestScriptRegistry"/> and a cached <see cref="FhirTargetCache"/>. The
/// evaluator and fixture providers are the same ones <see cref="Ignixa.ConformanceMatrix.Cli.Commands.RunCommand"/>
/// wires up for a one-shot run — only the hosting and per-request target/definition selection are new.
/// </summary>
internal static class RunnerHost
{
    public static WebApplication Create(
        TestScriptRegistry registry,
        FhirTargetCache targetCache,
        IFhirSchemaProvider schemaProvider,
        string hostIp,
        int port,
        string? defaultFhirVersion,
        Func<HttpClient, ITestRequestProvider>? providerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(targetCache);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var effectiveProviderFactory = providerFactory ?? (client => new HttpTestRequestProvider(client));
        var fixtureProvider = new CompositeFixtureProvider(
        [
            new FhirFakesFixtureProvider(),
            new InlineFixtureProvider()
        ]);

        var builder = WebApplication.CreateSlimBuilder();
        // Load runs iterate at high frequency; per-request Information logging would drown the
        // console in noise nobody watches during a run.
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var app = builder.Build();
        app.Urls.Add($"http://{hostIp}:{port}");

        app.MapGet("/healthz", () =>
            Results.Ok(new HealthResponse("ok", registry.Count, registry.InvalidCount)));

        app.MapGet("/testscripts", () =>
            Results.Ok(registry.Entries
                .Select(e => new TestScriptListEntry(e.Id, e.Name, e.RelativePath, e.ParseError is null, e.ParseError))
                .ToList()));

        app.MapPost("/run", (RunRequest request, CancellationToken cancellationToken) =>
            HandleRunAsync(request, registry, targetCache, schemaProvider, fixtureProvider, defaultFhirVersion, effectiveProviderFactory, cancellationToken));

        return app;
    }

    private static async Task<IResult> HandleRunAsync(
        RunRequest request,
        TestScriptRegistry registry,
        FhirTargetCache targetCache,
        IFhirSchemaProvider schemaProvider,
        IFixtureProvider fixtureProvider,
        string? defaultFhirVersion,
        Func<HttpClient, ITestRequestProvider> providerFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TestScriptId))
            return Results.BadRequest(new ErrorResponse("testScriptId is required"));

        if (string.IsNullOrWhiteSpace(request.FhirBaseUrl))
            return Results.BadRequest(new ErrorResponse("fhirBaseUrl is required"));

        if (!Uri.TryCreate(request.FhirBaseUrl, UriKind.Absolute, out _))
            return Results.BadRequest(new ErrorResponse($"fhirBaseUrl is not a valid absolute URI: {request.FhirBaseUrl}"));

        if (request.Mode is not ("performance" or "conformance"))
            return Results.BadRequest(new ErrorResponse($"mode must be 'performance' or 'conformance', got '{request.Mode}'"));

        var entry = registry.TryGet(request.TestScriptId);
        if (entry is null)
            return Results.NotFound(new ErrorResponse($"No TestScript registered with id '{request.TestScriptId}'"));

        if (entry.Definition is null)
            return Results.Json(
                new ErrorResponse($"TestScript '{request.TestScriptId}' failed to parse: {entry.ParseError}"),
                statusCode: StatusCodes.Status422UnprocessableEntity);

        var options = request.Options ?? new RunRequestOptions();
        var rewrite = DefinitionRewriter.Apply(entry.Definition, options);
        if (!rewrite.IsValid)
            return Results.BadRequest(new ErrorResponse(rewrite.Error!));

        var fhirVersion = request.FhirVersion ?? defaultFhirVersion;

        try
        {
            var target = targetCache.GetOrCreate(request.FhirBaseUrl, fhirVersion);
            var capabilityStatement = await target.GetCapabilityStatementAsync();
            var provider = providerFactory(target.HttpClient);
            var evaluator = new TestScriptEvaluator(provider, fixtureProvider, schemaProvider);

            var report = await evaluator.ExecuteAsync(
                rewrite.Definition!, cancellationToken, fhirVersion: fhirVersion, capabilityStatement: capabilityStatement);

            return Results.Ok(RunResponseMapper.Map(report, request.TestScriptId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Results.Json(
                new ErrorResponse($"{ex.GetType().Name}: {ex.Message}"),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
