using System.Diagnostics;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Reporting;
using Ignixa.TestScript.Validation;

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
        CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.UtcNow;
        var recorder = new TestScriptResultRecorder();

        var context = new TestScriptContext
        {
            Recorder = recorder
        };

        foreach (var variable in definition.Variables)
        {
            if (variable.DefaultValue is not null)
                context = context.WithVariable(variable.Name, variable.DefaultValue);
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
                var resource = await fixtureProvider.ResolveFixtureAsync(fixture, fixtureCtx, cancellationToken);
                if (resource is not null)
                {
                    context = context.WithFixture(fixture.Id, resource);

                    if (fixture.Autocreate)
                        context = await AutocreateFixtureAsync(fixture, resource, context, cancellationToken);
                }
                else
                {
                    recorder.RecordOperationResult($"fixture:{fixture.Id}", $"Resolve fixture '{fixture.Id}'",
                        new OperationOutcome(false, ErrorMessage: $"No provider resolved fixture '{fixture.Id}'"));
                }
            }

            foreach (var action in definition.Setup)
            {
                context = await action.AcceptAsync(this, context, cancellationToken);
                context = VariableExtractor.ExtractFromResponse(definition.Variables, context, schemaProvider);
            }

            recorder.EndPhase();
        }

        var setupFailed = recorder.SetupOutcome is TestScriptOutcome.Fail or TestScriptOutcome.Error;

        if (!setupFailed)
        {
            foreach (var test in definition.Tests)
            {
                recorder.BeginPhase(TestPhaseType.Test, test.Name, test.Description);
                context = await ExecuteActionsAsync(test.Actions, definition.Variables, context, cancellationToken);
                recorder.EndPhase();
            }
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
        try
        {
            if (expression.Destination is not null and > 1)
                throw new NotSupportedException(
                    $"Multi-server destinations are not supported. Destination '{expression.Destination}' was requested but only a single provider is configured.");

            var request = BuildRequest(expression, context);

            context = context.WithRequest(expression.RequestId, request);
            var response = await _provider.ExecuteAsync(request, cancellationToken);
            context = context.WithResponse(expression.ResponseId, response);

            sw.Stop();
            context.Recorder.RecordOperationResult(expression.Label, expression.Description,
                new OperationOutcome(true, response.StatusCode, Duration: sw.Elapsed));
            return context;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            context.Recorder.RecordOperationResult(expression.Label, expression.Description,
                new OperationOutcome(false, ErrorMessage: ex.Message, Duration: sw.Elapsed));
            return context;
        }
    }

    public ValueTask<TestScriptContext> VisitAssertAsync(
        AssertExpression expression,
        TestScriptContext context,
        CancellationToken cancellationToken)
    {
        var (passed, message) = EvaluateAssertionWithMessage(expression, context);
        context.Recorder.RecordAssertionResult(expression.Label, expression.Description,
            new AssertionOutcome(passed, expression.WarningOnly, passed ? null : message));
        return ValueTask.FromResult(context);
    }

    private async Task<TestScriptContext> AutocreateFixtureAsync(
        FixtureDefinition fixture, ResourceJsonNode resource,
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
            var success = response.StatusCode is >= 200 and < 300;
            context.Recorder.RecordOperationResult(
                $"fixture:{fixture.Id}",
                $"Autocreate fixture '{fixture.Id}'",
                success
                    ? new OperationOutcome(true, response.StatusCode)
                    : new OperationOutcome(false, response.StatusCode, ErrorMessage: $"Autocreate returned HTTP {response.StatusCode}"));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            context.Recorder.RecordOperationResult(
                $"fixture:{fixture.Id}",
                $"Autocreate fixture '{fixture.Id}'",
                new OperationOutcome(false, ErrorMessage: ex.Message));
        }
        return context;
    }

    private async Task<TestScriptContext> AutodeleteFixtureAsync(
        FixtureDefinition fixture, TestScriptContext context, CancellationToken cancellationToken)
    {
        if (!context.Fixtures.TryGetValue(fixture.Id, out var resource)) return context;
        if (string.IsNullOrEmpty(resource.Id)) return context;

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
                    ? new OperationOutcome(true, response.StatusCode)
                    : new OperationOutcome(false, response.StatusCode, ErrorMessage: $"Autodelete returned HTTP {response.StatusCode}"));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            context.Recorder.RecordOperationResult(
                $"fixture:{fixture.Id}",
                $"Autodelete fixture '{fixture.Id}'",
                new OperationOutcome(false, ErrorMessage: ex.Message));
        }
        return context;
    }

    private static TestRequest BuildRequest(OperationExpression op, TestScriptContext context)
    {
        var method = op.Method ?? DeriveMethod(op.Type);
        var url = BuildUrl(op, context);

        ResourceJsonNode? body = null;
        if (op.SourceId is not null && context.Fixtures.TryGetValue(op.SourceId, out var fixture))
            body = JsonSourceNodeFactory.Parse(fixture.MutableNode.DeepClone());

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (op.Accept is not null) headers["Accept"] = op.Accept;
        if (op.ContentType is not null) headers["Content-Type"] = op.ContentType;
        foreach (var h in op.Headers)
            headers[VariableResolver.Resolve(h.Field, context)] = VariableResolver.Resolve(h.Value, context);

        return new TestRequest { Method = method, Url = url, Body = body, Headers = headers };
    }

    private static string BuildUrl(OperationExpression op, TestScriptContext context)
    {
        if (op.Url is not null)
            return VariableResolver.Resolve(op.Url, context);

        var resource = op.Resource ?? string.Empty;
        var parameters = VariableResolver.ResolveIfNotNull(op.Params, context) ?? string.Empty;

        return $"{resource}{parameters}";
    }

    private static HttpMethod DeriveMethod(string operationType) => operationType switch
    {
        "create" => HttpMethod.Post,
        "read" or "vread" or "search" or "history" => HttpMethod.Get,
        "update" => HttpMethod.Put,
        "patch" => HttpMethod.Patch,
        "delete" => HttpMethod.Delete,
        _ => throw new InvalidOperationException(
            $"Unknown operation type: '{operationType}'. Expected: create, read, vread, update, patch, delete, search, history")
    };

    private static (bool Passed, string? Message) EvaluateAssertionWithMessage(
        AssertExpression assertion, TestScriptContext context)
    {
        return assertion.Criteria switch
        {
            ResponseStatusCriteria c => EvaluateResponseStatus(c, context),
            ResponseCodeCriteria c => EvaluateResponseCode(c, context),
            ContentTypeCriteria c => EvaluateContentType(c, context),
            ResourceTypeCriteria c => EvaluateResourceType(c, context),
            HeaderCriteria c => EvaluateHeader(c, context),
            FhirPathCriteria c => (false, $"FHIRPath expression assertions are not yet implemented: '{c.Expression}'"),
            RequestMethodCriteria c => EvaluateRequestMethod(c, assertion, context),
            RequestUrlCriteria c => EvaluateRequestUrl(c, assertion, context),
            _ => throw new InvalidOperationException($"Unhandled assertion criteria type: {assertion.Criteria.GetType().Name}")
        };
    }

    private static (bool, string?) EvaluateResponseStatus(ResponseStatusCriteria c, TestScriptContext context)
    {
        var response = context.LastResponse;
        if (response is null)
            return (false, "No response available to assert against");

        var matched = MatchesResponseCode(c.Status, response.StatusCode);
        return (matched, matched ? null : $"Expected response '{c.Status}' but got status {response.StatusCode}");
    }

    private static (bool, string?) EvaluateResponseCode(ResponseCodeCriteria c, TestScriptContext context)
    {
        var response = context.LastResponse;
        if (response is null)
            return (false, "No response available to assert against");

        var passed = response.StatusCode.ToString() == c.Code;
        return (passed, passed ? null : $"Expected responseCode '{c.Code}' but got {response.StatusCode}");
    }

    private static (bool, string?) EvaluateContentType(ContentTypeCriteria c, TestScriptContext context)
    {
        var response = context.LastResponse;
        if (response is null)
            return (false, "No response available to assert against");

        var actual = response.Headers.GetValueOrDefault("Content-Type");
        var passed = string.Equals(actual, c.ContentType, StringComparison.OrdinalIgnoreCase);
        return (passed, passed ? null : $"Expected content type '{c.ContentType}' but got '{actual}'");
    }

    private static (bool, string?) EvaluateResourceType(ResourceTypeCriteria c, TestScriptContext context)
    {
        var response = context.LastResponse;
        if (response?.Body is null)
            return (false, "No response body available to assert against");

        var actual = response.Body.ResourceType;
        var passed = actual == c.ResourceType;
        return (passed, passed ? null : $"Expected resource type '{c.ResourceType}' but got '{actual}'");
    }

    private static (bool, string?) EvaluateHeader(HeaderCriteria c, TestScriptContext context)
    {
        var response = context.LastResponse;
        if (response is null)
            return (false, "No response available to assert against");

        var actual = response.Headers.GetValueOrDefault(c.Field);
        var op = c.Operator ?? (c.Value is null ? AssertOperator.NotEmpty : AssertOperator.Equals);
        var passed = EvaluateWithOperator(actual, c.Value, op);
        return (passed, passed ? null : $"Header '{c.Field}' value '{actual}' did not match expected '{c.Value}' with operator {op}");
    }

    private static (bool, string?) EvaluateRequestMethod(RequestMethodCriteria c, AssertExpression assertion, TestScriptContext context)
    {
        var request = assertion.SourceId is not null
            ? context.RequestHistory.GetValueOrDefault(assertion.SourceId)
            : context.LastRequest;

        if (request is null)
            return (false, "No request available to assert against");

        var actualMethod = request.Method.Method;
        var passed = string.Equals(actualMethod, c.Method, StringComparison.OrdinalIgnoreCase);
        return (passed, passed ? null : $"Expected request method '{c.Method}' but was '{actualMethod}'");
    }

    private static (bool, string?) EvaluateRequestUrl(RequestUrlCriteria c, AssertExpression assertion, TestScriptContext context)
    {
        var request = assertion.SourceId is not null
            ? context.RequestHistory.GetValueOrDefault(assertion.SourceId)
            : context.LastRequest;

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
            AssertOperator.GreaterThan => string.Compare(actual, expected, StringComparison.Ordinal) > 0,
            AssertOperator.LessThan => string.Compare(actual, expected, StringComparison.Ordinal) < 0,
            _ => throw new InvalidOperationException($"Unhandled assert operator: {op}")
        };
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
