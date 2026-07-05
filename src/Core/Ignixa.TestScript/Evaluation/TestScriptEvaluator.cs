using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Reporting;
using Ignixa.TestScript.Validation;
using Semver;

namespace Ignixa.TestScript.Evaluation;

public sealed class TestScriptEvaluator(
    ITestRequestProvider provider,
    IFixtureProvider fixtureProvider,
    IFhirSchemaProvider schemaProvider,
    IFhirResourceValidator? validator = null) : ITestScriptActionVisitor
{
    private readonly ITestRequestProvider _provider = provider;

    internal IFhirResourceValidator Validator { get; } = validator ?? new NoOpValidator();

    public async Task<TestScriptReport> ExecuteAsync(
        TestScriptDefinition definition,
        CancellationToken cancellationToken,
        string? fhirVersion = null,
        ResourceJsonNode? capabilityStatement = null)
    {
        var startTime = DateTimeOffset.UtcNow;
        var recorder = new TestScriptResultRecorder();
        var capabilityElement = capabilityStatement?.ToElement(schemaProvider);

        var context = new TestScriptContext
        {
            Recorder = recorder
        };

        foreach (var variable in definition.Variables)
        {
            if (variable.DefaultValue is not null)
                context = context.WithVariable(variable.Name, variable.DefaultValue);
        }

        var (suiteCapabilityMet, suiteCapabilityReason) =
            EvaluateCapabilityRequirement(definition.Metadata.RequiresCapability, capabilityElement);
        if (!suiteCapabilityMet)
        {
            var reason = suiteCapabilityReason ?? "Required capability not met";
            foreach (var test in definition.Tests)
                RecordSkippedTest(recorder, test, reason);

            return recorder.Build(definition.Metadata.Name, startTime, DateTimeOffset.UtcNow);
        }

        var hasSetupWork = definition.Fixtures.Count > 0 || definition.Setup.Count > 0;
        if (hasSetupWork)
        {
            recorder.BeginPhase(TestPhaseType.Setup);

            foreach (var fixture in definition.Fixtures)
            {
                var fixtureCtx = new FixtureResolutionContext
                {
                    Schema = schemaProvider,
                    ResourceType = fixture.Resource?.ResourceType
                };
                try
                {
                    var resource = await fixtureProvider.ResolveFixtureAsync(fixture, fixtureCtx, cancellationToken);
                    if (resource is not null)
                    {
                        context = context.WithFixture(fixture.Id, resource);

                        if (fixture.Autocreate)
                            context = await AutocreateFixtureAsync(fixture, resource, definition.Variables, context, cancellationToken);
                    }
                    else
                    {
                        recorder.RecordOperationResult($"fixture:{fixture.Id}", $"Resolve fixture '{fixture.Id}'",
                            new OperationOutcome(false, ErrorMessage: $"No provider resolved fixture '{fixture.Id}'"));
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    recorder.RecordOperationResult($"fixture:{fixture.Id}", $"Resolve fixture '{fixture.Id}'",
                        new OperationOutcome(false, ErrorMessage: ex.Message));
                }
            }

            context = await ExecuteActionsAsync(definition.Setup, definition.Variables, context, cancellationToken);

            recorder.EndPhase();
        }

        var setupFailed = recorder.SetupOutcome is TestScriptOutcome.Fail or TestScriptOutcome.Error;

        if (!setupFailed)
        {
            foreach (var test in definition.Tests)
            {
                if (!IsVersionCompatible(test.FhirVersions, fhirVersion))
                {
                    RecordSkippedTest(recorder, test, VersionMismatchReason(test.FhirVersions, fhirVersion));
                    continue;
                }

                var (testCapabilityMet, testCapabilityReason) =
                    EvaluateCapabilityRequirement(test.RequiresCapability, capabilityElement);
                if (!testCapabilityMet)
                {
                    RecordSkippedTest(recorder, test, testCapabilityReason ?? "Required capability not met");
                    continue;
                }

                if (test.Parameters is null)
                {
                    recorder.BeginPhase(TestPhaseType.Test, test.Name, test.Description);
                    context = await ExecuteActionsAsync(test.Actions, definition.Variables, context, cancellationToken);
                    recorder.EndPhase();
                }
                else
                {
                    var param = test.Parameters;
                    foreach (var value in param.Values)
                    {
                        var iterationContext = context.WithVariable(param.VariableName, value);
                        recorder.BeginPhase(TestPhaseType.Test, $"{test.Name} [{value}]", test.Description);
                        await ExecuteActionsAsync(test.Actions, definition.Variables, iterationContext, cancellationToken);
                        recorder.EndPhase();
                    }
                }
            }
        }
        else
        {
            foreach (var test in definition.Tests)
                RecordSkippedTest(recorder, test, "setup failed");
        }

        if (definition.Teardown.Count > 0 || definition.Fixtures.Any(f => f.Autodelete))
        {
            recorder.BeginPhase(TestPhaseType.Teardown);

            foreach (var fixture in definition.Fixtures)
            {
                if (fixture.Autodelete)
                    context = await AutodeleteFixtureAsync(fixture, context, cancellationToken);
            }

            foreach (var action in definition.Teardown)
            {
                context = await action.AcceptAsync(this, context, cancellationToken);
                context = VariableExtractor.ExtractFromResponse(definition.Variables, context, schemaProvider);
            }
            recorder.EndPhase();
        }

        return recorder.Build(definition.Metadata.Name, startTime, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Matches a test's declared <c>fhirVersions</c> spec tokens against the actual FHIR version
    /// being targeted. Each spec token is checked two ways: an exact case-insensitive string match
    /// (preserving today's behavior for existing suites, e.g. "4.0" == "4.0"), or a granular
    /// numeric Major/Minor/Patch comparison that supports "4.*" (major-only), "4.0" (major.minor),
    /// and "4.0.1" (exact) against a semver-parsed <paramref name="fhirVersion"/>. Prerelease and
    /// build metadata are ignored by the granular comparison. Unparseable input fails safe (no
    /// match via that path) rather than throwing. An empty <paramref name="fhirVersions"/> list or
    /// a <see langword="null"/> <paramref name="fhirVersion"/> short-circuits to a match (the test
    /// runs) rather than a mismatch, so gating never turns into a silent skip when either side of
    /// the comparison is unknown.
    /// </summary>
    private static bool IsVersionCompatible(IReadOnlyList<string> fhirVersions, string? fhirVersion)
    {
        if (fhirVersions.Count == 0) return true;
        if (fhirVersion is null) return true;

        var parsedActual = SemVersion.TryParse(fhirVersion, SemVersionStyles.Any, out var actual) ? actual : null;

        foreach (var spec in fhirVersions)
        {
            if (string.Equals(spec, fhirVersion, StringComparison.OrdinalIgnoreCase))
                return true;

            if (parsedActual is not null && MatchesVersionSpec(spec, parsedActual))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds the skip reason for a version mismatch, distinguishing a legitimate scoping mismatch
    /// (at least one <paramref name="fhirVersions"/> token parses as a recognizable version spec,
    /// it just doesn't match <paramref name="fhirVersion"/>) from every token being unrecognizable
    /// (likely an authoring typo, e.g. "4,0" or "r4") — mirroring how
    /// <see cref="EvaluateCapabilityRequirement"/> already distinguishes "not met" from "malformed"
    /// so a typo doesn't read identically to an intentional version restriction.
    /// </summary>
    private static string VersionMismatchReason(IReadOnlyList<string> fhirVersions, string? fhirVersion)
    {
        var anyTokenRecognized = fhirVersions.Any(spec => TryParseVersionSpec(spec, out _, out _, out _));
        return anyTokenRecognized
            ? $"Test targets FHIR version(s) [{string.Join(", ", fhirVersions)}] but execution requested '{fhirVersion}'"
            : $"Test's fhirVersions [{string.Join(", ", fhirVersions)}] contain no recognizable version spec — check for a typo";
    }

    private static bool MatchesVersionSpec(string? spec, SemVersion actual)
    {
        if (!TryParseVersionSpec(spec, out var major, out var minor, out var patch))
            return false;

        if (actual.Major != major) return false;
        if (minor is not null && actual.Minor != minor) return false;
        if (patch is not null && actual.Patch != patch) return false;
        return true;
    }

    /// <summary>
    /// Parses a spec token such as "4", "4.*", "4.0", or "4.0.1" into 1-3 numeric components.
    /// A trailing "*" segment is dropped (it's the major-only wildcard marker). Returns false for
    /// <see langword="null"/> or anything that isn't 1-3 non-negative integer segments, so
    /// malformed tokens simply don't match via the granular path rather than throwing.
    /// </summary>
    private static bool TryParseVersionSpec(string? spec, out int major, out int? minor, out int? patch)
    {
        major = 0;
        minor = null;
        patch = null;

        if (spec is null) return false;

        var segments = spec.Split('.');
        if (segments.Length > 0 && segments[^1] == "*")
            segments = segments[..^1];

        if (segments.Length is 0 or > 3) return false;

        var values = new int?[3];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!int.TryParse(segments[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                return false;
            values[i] = value;
        }

        major = values[0]!.Value;
        minor = values[1];
        patch = values[2];
        return true;
    }

    /// <summary>
    /// Evaluates a <c>requiresCapability</c> FHIRPath expression against the target's
    /// CapabilityStatement, converted once per <see cref="ExecuteAsync"/> call by the caller.
    /// A missing/empty expression always passes. A missing <paramref name="capabilityElement"/>
    /// (couldn't be fetched or wasn't supplied) also passes — capability gating fails open
    /// rather than silently skipping large parts of a run because <c>/metadata</c> happened
    /// to be unreachable. A malformed expression fails closed (skip) with the exception message
    /// as the reason, since that's an authoring bug rather than a target-server characteristic.
    /// </summary>
    private static (bool IsMet, string? Reason) EvaluateCapabilityRequirement(
        string? requiresCapability, IElement? capabilityElement)
    {
        if (string.IsNullOrWhiteSpace(requiresCapability)) return (true, null);
        if (capabilityElement is null) return (true, null);

        try
        {
            var met = capabilityElement.IsTrue(requiresCapability);
            return (met, met ? null : $"Required capability not met: {requiresCapability}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, $"requiresCapability expression '{requiresCapability}' failed to evaluate: {ex.Message}");
        }
    }

    private static void RecordSkippedTest(TestScriptResultRecorder recorder, TestPhaseDefinition test, string reason)
    {
        if (test.Parameters is null)
        {
            recorder.RecordSkippedTest(test.Name, test.Description, reason);
        }
        else
        {
            foreach (var value in test.Parameters.Values)
                recorder.RecordSkippedTest($"{test.Name} [{value}]", test.Description, reason);
        }
    }

    private async Task<TestScriptContext> ExecuteActionsAsync(
        IReadOnlyList<ActionExpression> actions,
        IReadOnlyList<VariableDefinition> variables,
        TestScriptContext context,
        CancellationToken cancellationToken)
    {
        foreach (var action in actions)
        {
            context = await action.AcceptAsync(this, context, cancellationToken);
            if (action is OperationExpression)
                context = VariableExtractor.ExtractFromResponse(variables, context, schemaProvider);
        }

        return context;
    }

    public async ValueTask<TestScriptContext> VisitOperationAsync(
        OperationExpression expression,
        TestScriptContext context,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        TestRequest? request = null;
        try
        {
            if (expression.Destination is not null and > 1)
                throw new NotSupportedException(
                    $"Multi-server destinations are not supported. Destination '{expression.Destination}' was requested but only a single provider is configured.");

            if (!expression.EncodeRequestUrl)
                context.Recorder.RecordAssertionResult(expression.Label, expression.Description,
                    new AssertionOutcome(false, WarningOnly: true,
                        "encodeRequestUrl=false is not supported; URL was encoded"));

            request = BuildRequest(expression, context);

            context = context.WithRequest(expression.RequestId, request);
            var response = await _provider.ExecuteAsync(request, cancellationToken);
            context = context.WithResponse(expression.ResponseId, response);

            sw.Stop();
            context.Recorder.RecordOperationResult(expression.Label, expression.Description,
                new OperationOutcome(true, response.StatusCode, Duration: sw.Elapsed, Request: request, Response: response));
            return context;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            throw;
        }
        catch (OperationCanceledException ex)
        {
            sw.Stop();
            context.Recorder.RecordOperationResult(expression.Label, expression.Description,
                new OperationOutcome(false, ErrorMessage: $"Request timed out or was aborted: {ex.Message}", Duration: sw.Elapsed, Request: request));
            return context;
        }
        catch (Exception ex)
        {
            sw.Stop();
            context.Recorder.RecordOperationResult(expression.Label, expression.Description,
                new OperationOutcome(false, ErrorMessage: ex.Message, Duration: sw.Elapsed, Request: request));
            return context;
        }
    }

    public ValueTask<TestScriptContext> VisitAssertAsync(
        AssertExpression expression,
        TestScriptContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var (passed, message) = EvaluateAssertionWithMessage(expression, context);
            context.Recorder.RecordAssertionResult(expression.Label, expression.Description,
                new AssertionOutcome(passed, expression.WarningOnly, passed ? null : message));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.Recorder.RecordAssertionResult(expression.Label, expression.Description,
                new AssertionOutcome(false, expression.WarningOnly, ex.Message, IsError: true));
        }
        return ValueTask.FromResult(context);
    }

    private async Task<TestScriptContext> AutocreateFixtureAsync(
        FixtureDefinition fixture, ResourceJsonNode resource,
        IReadOnlyList<VariableDefinition> variables,
        TestScriptContext context, CancellationToken cancellationToken)
    {
        var url = $"{resource.ResourceType}";
        var request = new TestRequest { Method = HttpMethod.Post, Url = url, Body = resource };
        context = context.WithRequest(null, request);
        try
        {
            var response = await _provider.ExecuteAsync(request, cancellationToken);
            context = context.WithResponse(null, response);
            if (response.Body is not null)
                context = context.WithFixture(fixture.Id, response.Body);
            context = context.WithResponse(fixture.Id, response);
            context = VariableExtractor.ExtractFromResponse(variables, context, schemaProvider);
            var success = response.StatusCode is >= 200 and < 300;
            context.Recorder.RecordOperationResult(
                $"fixture:{fixture.Id}",
                $"Autocreate fixture '{fixture.Id}'",
                success
                    ? new OperationOutcome(true, response.StatusCode, Request: request, Response: response)
                    : new OperationOutcome(
                        false,
                        response.StatusCode,
                        ErrorMessage: $"Autocreate returned HTTP {response.StatusCode}",
                        Request: request,
                        Response: response));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            context.Recorder.RecordOperationResult(
                $"fixture:{fixture.Id}",
                $"Autocreate fixture '{fixture.Id}'",
                new OperationOutcome(false, ErrorMessage: ex.Message, Request: request));
        }
        return context;
    }

    private async Task<TestScriptContext> AutodeleteFixtureAsync(
        FixtureDefinition fixture, TestScriptContext context, CancellationToken cancellationToken)
    {
        if (!context.Fixtures.TryGetValue(fixture.Id, out var resource))
        {
            context.Recorder.RecordOperationResult(
                $"fixture:{fixture.Id}", $"Autodelete fixture '{fixture.Id}'",
                new OperationOutcome(false, ErrorMessage: $"Fixture '{fixture.Id}' was not in context; resource may not have been created"));
            return context;
        }
        if (string.IsNullOrEmpty(resource.Id))
        {
            context.Recorder.RecordOperationResult(
                $"fixture:{fixture.Id}", $"Autodelete fixture '{fixture.Id}'",
                new OperationOutcome(false, ErrorMessage: $"Fixture '{fixture.Id}' has no server-assigned id; resource may leak on the server"));
            return context;
        }

        var url = $"{resource.ResourceType}/{resource.Id}";
        var request = new TestRequest { Method = HttpMethod.Delete, Url = url };
        context = context.WithRequest(null, request);
        try
        {
            var response = await _provider.ExecuteAsync(request, cancellationToken);
            context = context.WithResponse(null, response);
            var success = response.StatusCode is >= 200 and < 300;
            context.Recorder.RecordOperationResult(
                $"fixture:{fixture.Id}",
                $"Autodelete fixture '{fixture.Id}'",
                success
                    ? new OperationOutcome(true, response.StatusCode, Request: request, Response: response)
                    : new OperationOutcome(
                        false,
                        response.StatusCode,
                        ErrorMessage: $"Autodelete returned HTTP {response.StatusCode}",
                        Request: request,
                        Response: response));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            context.Recorder.RecordOperationResult(
                $"fixture:{fixture.Id}",
                $"Autodelete fixture '{fixture.Id}'",
                new OperationOutcome(false, ErrorMessage: ex.Message, Request: request));
        }
        return context;
    }

    private static TestRequest BuildRequest(OperationExpression op, TestScriptContext context)
    {
        var method = op.Method ?? DeriveMethod(op.Type);
        var url = BuildUrl(op, context);

        ResourceJsonNode? body = null;
        string? formBody = null;

        var isFormSearch = op.Type == "search" && method == HttpMethod.Post;
        if (isFormSearch)
        {
            var rawParams = VariableResolver.ResolveIfNotNull(op.Params, context);
            formBody = rawParams?.TrimStart('?');
        }
        else if (op.SourceId is not null)
        {
            if (context.Fixtures.TryGetValue(op.SourceId, out var fixture))
                body = fixture;
            else if (context.ResponseHistory.TryGetValue(op.SourceId, out var prevResponse))
                body = prevResponse.Body;
            else
                throw new InvalidOperationException(
                    $"sourceId '{op.SourceId}' refers to no known fixture or response");
        }

        var headers = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        if (op.Accept is not null) headers["Accept"] = op.Accept;
        if (op.ContentType is not null) headers["Content-Type"] = op.ContentType;
        foreach (var h in op.Headers)
            headers[VariableResolver.Resolve(h.Field, context)] = VariableResolver.Resolve(h.Value, context);

        return new TestRequest { Method = method, Url = url, Body = body, FormBody = formBody, Headers = headers.ToImmutable() };
    }

    private static string BuildUrl(OperationExpression op, TestScriptContext context)
    {
        if (op.Url is not null)
            return VariableResolver.Resolve(op.Url, context);

        var resource = op.Resource ?? string.Empty;

        if (op.Type == "search" && op.Method == HttpMethod.Post)
            return $"{resource}/_search";

        var parameters = VariableResolver.ResolveIfNotNull(op.Params, context) ?? string.Empty;

        // FHIR operations ($validate, $expand, ...) are invoked at [type]/$op (type-level) or
        // /$op (system-level), not as a plain POST to the resource type. Without this, a
        // $validate operation would POST the body to /[type] and create the resource instead.
        if (op.Type.StartsWith('$'))
        {
            var opPath = string.IsNullOrEmpty(resource) ? op.Type : $"{resource}/{op.Type}";
            return $"{opPath}{parameters}";
        }

        return $"{resource}{parameters}";
    }

    private static HttpMethod DeriveMethod(string operationType) => operationType switch
    {
        "create" => HttpMethod.Post,
        "read" or "vread" or "search" or "history" or "capabilities" or "conforms" => HttpMethod.Get,
        "update" or "updateCreate" => HttpMethod.Put,
        "patch" => HttpMethod.Patch,
        "delete" => HttpMethod.Delete,
        _ => HttpMethod.Post
    };

    private (bool Passed, string? Message) EvaluateAssertionWithMessage(
        AssertExpression assertion, TestScriptContext context)
    {
        return assertion.Criteria switch
        {
            ResponseStatusCriteria c => EvaluateResponseStatus(c, assertion, context),
            ResponseCodeCriteria c => EvaluateResponseCode(c, assertion, context),
            ContentTypeCriteria c => EvaluateContentType(c, assertion, context),
            ResourceTypeCriteria c => EvaluateResourceType(c, assertion, context),
            HeaderCriteria c => EvaluateHeader(c, assertion, context),
            FhirPathCriteria c => EvaluateFhirPath(c, assertion, context),
            FhirPathValueCriteria c => EvaluateFhirPathValue(c, assertion, context),
            RequestMethodCriteria c => EvaluateRequestMethod(c, assertion, context),
            RequestUrlCriteria c => EvaluateRequestUrl(c, assertion, context),
            _ => throw new InvalidOperationException($"Unhandled assertion criteria type: {assertion.Criteria.GetType().Name}")
        };
    }

    private static ResourceJsonNode? ResolveAssertionBody(AssertExpression assertion, TestScriptContext context)
    {
        if (assertion.Direction == AssertDirection.Request)
            return ResolveAssertionRequest(assertion, context)?.Body;

        return ResolveAssertionResponse(assertion, context)?.Body;
    }

    private static TestResponse? ResolveAssertionResponse(AssertExpression assertion, TestScriptContext context)
    {
        if (assertion.Direction == AssertDirection.Request) return null;
        if (assertion.SourceId is null) return context.LastResponse;
        if (context.ResponseHistory.TryGetValue(assertion.SourceId, out var response)) return response;

        throw new InvalidOperationException(
            $"Assertion sourceId '{assertion.SourceId}' refers to no known response");
    }

    private static TestRequest? ResolveAssertionRequest(AssertExpression assertion, TestScriptContext context)
    {
        if (assertion.SourceId is null) return context.LastRequest;
        if (context.RequestHistory.TryGetValue(assertion.SourceId, out var request)) return request;

        throw new InvalidOperationException(
            $"Assertion sourceId '{assertion.SourceId}' refers to no known request");
    }

    private (bool, string?) EvaluateFhirPath(FhirPathCriteria c, AssertExpression assertion, TestScriptContext context)
    {
        var body = ResolveAssertionBody(assertion, context);
        if (body is null)
        {
            var parseError = ResolveAssertionResponse(assertion, context)?.BodyParseError;
            var reason = parseError is not null
                ? $"Response body was not valid JSON: {parseError}"
                : "No response body available to assert against with FHIRPath";
            return (false, reason);
        }

        var resolvedExpr = VariableResolver.Resolve(c.Expression, context);
        var result = body.ToElement(schemaProvider).IsTrue(resolvedExpr);
        return (result, result ? null : $"FHIRPath expression '{resolvedExpr}' did not evaluate to true");
    }

    private (bool, string?) EvaluateFhirPathValue(FhirPathValueCriteria c, AssertExpression assertion, TestScriptContext context)
    {
        var body = ResolveAssertionBody(assertion, context);
        if (body is null)
        {
            var parseError = ResolveAssertionResponse(assertion, context)?.BodyParseError;
            var reason = parseError is not null
                ? $"Response body was not valid JSON: {parseError}"
                : "No response body available to assert against with FHIRPath";
            return (false, reason);
        }

        var resolvedExpr = VariableResolver.Resolve(c.Expression, context);
        var resolvedValue = VariableResolver.Resolve(c.Value ?? string.Empty, context);
        var actual = body.ToElement(schemaProvider).Select(resolvedExpr).AsString();

        var passed = EvaluateWithOperator(actual, resolvedValue, c.Operator);
        return (passed, passed
            ? null
            : $"FHIRPath expression '{resolvedExpr}' value '{actual}' did not match expected '{resolvedValue}' with operator {c.Operator}");
    }

    private static (bool, string?) EvaluateResponseStatus(ResponseStatusCriteria c, AssertExpression assertion, TestScriptContext context)
    {
        var response = ResolveAssertionResponse(assertion, context);
        if (response is null)
            return (false, "No response available to assert against");

        var matched = MatchesResponseCode(c.Status, response.StatusCode);
        return (matched, matched ? null : $"Expected response '{c.Status}' but got status {response.StatusCode}");
    }

    private static (bool, string?) EvaluateResponseCode(ResponseCodeCriteria c, AssertExpression assertion, TestScriptContext context)
    {
        var response = ResolveAssertionResponse(assertion, context);
        if (response is null)
            return (false, "No response available to assert against");

        var passed = response.StatusCode.ToString() == c.Code;
        return (passed, passed ? null : $"Expected responseCode '{c.Code}' but got {response.StatusCode}");
    }

    private static (bool, string?) EvaluateContentType(ContentTypeCriteria c, AssertExpression assertion, TestScriptContext context)
    {
        var response = ResolveAssertionResponse(assertion, context);
        if (response is null)
            return (false, "No response available to assert against");

        var actual = response.Headers.GetValueOrDefault("Content-Type");
        var passed = string.Equals(MediaTypeOf(actual), MediaTypeOf(c.ContentType), StringComparison.OrdinalIgnoreCase);
        return (passed, passed ? null : $"Expected content type '{c.ContentType}' but got '{actual}'");
    }

    private static string? MediaTypeOf(string? headerValue) =>
        headerValue?.Split(';', 2)[0].Trim();

    private static (bool, string?) EvaluateResourceType(ResourceTypeCriteria c, AssertExpression assertion, TestScriptContext context)
    {
        var response = ResolveAssertionResponse(assertion, context);
        if (response?.Body is null)
        {
            var reason = response?.BodyParseError is { } parseError
                ? $"Response body was not valid JSON: {parseError}"
                : "No response body available to assert against";
            return (false, reason);
        }

        var actual = response.Body.ResourceType;
        var passed = actual == c.ResourceType;
        return (passed, passed ? null : $"Expected resource type '{c.ResourceType}' but got '{actual}'");
    }

    private static (bool, string?) EvaluateHeader(HeaderCriteria c, AssertExpression assertion, TestScriptContext context)
    {
        var response = ResolveAssertionResponse(assertion, context);
        if (response is null)
            return (false, "No response available to assert against");

        var actual = response.Headers.GetValueOrDefault(c.Field);
        var op = c.Operator ?? (c.Value is null ? AssertOperator.NotEmpty : AssertOperator.Equals);
        var passed = EvaluateWithOperator(actual, c.Value, op);
        return (passed, passed ? null : $"Header '{c.Field}' value '{actual}' did not match expected '{c.Value}' with operator {op}");
    }

    private static (bool, string?) EvaluateRequestMethod(RequestMethodCriteria c, AssertExpression assertion, TestScriptContext context)
    {
        var request = ResolveAssertionRequest(assertion, context);
        if (request is null)
            return (false, "No request available to assert against");

        var actualMethod = request.Method.Method;
        var passed = string.Equals(actualMethod, c.Method, StringComparison.OrdinalIgnoreCase);
        return (passed, passed ? null : $"Expected request method '{c.Method}' but was '{actualMethod}'");
    }

    private static (bool, string?) EvaluateRequestUrl(RequestUrlCriteria c, AssertExpression assertion, TestScriptContext context)
    {
        var request = ResolveAssertionRequest(assertion, context);
        if (request is null)
            return (false, "No request available to assert against");

        var actualUrl = request.Url;
        var passed = EvaluateWithOperator(actualUrl, c.Url, c.Operator ?? AssertOperator.Equals);
        return (passed, passed ? null : $"Expected request URL '{c.Url}' but was '{actualUrl}'");
    }

    private static bool EvaluateWithOperator(string? actual, string? expected, AssertOperator op)
    {
        return op switch
        {
            AssertOperator.Equals => actual == expected,
            AssertOperator.NotEquals => actual != expected,
            AssertOperator.Contains => actual?.Contains(expected ?? string.Empty, StringComparison.Ordinal) ?? false,
            AssertOperator.NotContains => !(actual?.Contains(expected ?? string.Empty, StringComparison.Ordinal) ?? false),
            AssertOperator.In => expected?.Split(',').Select(s => s.Trim()).Contains(actual) ?? false,
            AssertOperator.NotIn => !(expected?.Split(',').Select(s => s.Trim()).Contains(actual) ?? false),
            AssertOperator.Empty => string.IsNullOrEmpty(actual),
            AssertOperator.NotEmpty => !string.IsNullOrEmpty(actual),
            AssertOperator.GreaterThan => CompareOrdered(actual, expected) > 0,
            AssertOperator.LessThan => CompareOrdered(actual, expected) < 0,
            _ => throw new InvalidOperationException($"Unhandled assert operator: {op}")
        };
    }

    private static int CompareOrdered(string? actual, string? expected)
    {
        if (decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var a) &&
            decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out var e))
            return a.CompareTo(e);

        return string.Compare(actual, expected, StringComparison.Ordinal);
    }

    private static bool MatchesResponseCode(string response, int statusCode) => response switch
    {
        "okay" => statusCode is >= 200 and < 300,
        "created" => statusCode == 201,
        "noContent" => statusCode == 204,
        "notModified" => statusCode == 304,
        "bad" => statusCode == 400,
        "forbidden" => statusCode == 403,
        "notFound" => statusCode == 404,
        "methodNotAllowed" => statusCode == 405,
        "conflict" => statusCode == 409,
        "gone" => statusCode == 410,
        "preconditionFailed" => statusCode == 412,
        "unprocessable" => statusCode == 422,
        _ => false
    };
}
