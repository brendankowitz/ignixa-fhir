using System.Diagnostics;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Reporting;
using Ignixa.TestScript.Validation;

namespace Ignixa.TestScript.Evaluation;

public sealed class TestScriptEvaluator(
    IFhirClientRegistry clientRegistry,
    IFixtureProvider fixtureProvider,
    IFhirSchemaProvider schemaProvider,
    IFhirResourceValidator? validator = null) : ITestScriptActionVisitor
{
    internal IFhirResourceValidator Validator { get; } = validator ?? new NoOpValidator();

    public async Task<TestScriptReport> ExecuteAsync(
        TestScriptDefinition definition,
        CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.UtcNow;
        var recorder = new TestScriptResultRecorder();

        var context = new TestScriptContext
        {
            ClientRegistry = clientRegistry,
            Recorder = recorder
        };

        var fixtureCtx = new FixtureResolutionContext { Schema = schemaProvider };
        foreach (var fixture in definition.Fixtures)
        {
            var resource = await fixtureProvider.ResolveFixtureAsync(fixture, fixtureCtx, cancellationToken);
            if (resource is not null)
                context = context.WithFixture(fixture.Id, resource);
        }

        foreach (var variable in definition.Variables)
        {
            if (variable.DefaultValue is not null)
                context = context.WithVariable(variable.Name, variable.DefaultValue);
        }

        if (definition.Setup.Count > 0)
        {
            recorder.BeginPhase(TestPhaseType.Setup);
            context = await ExecuteActionsAsync(definition.Setup, context, cancellationToken);
            recorder.EndPhase();
        }

        var setupFailed = definition.Setup.Count > 0 &&
            recorder.Build(definition.Metadata.Name, startTime, DateTimeOffset.UtcNow)
                .SetupResult?.Outcome is TestScriptOutcome.Fail or TestScriptOutcome.Error;

        if (!setupFailed)
        {
            foreach (var test in definition.Tests)
            {
                recorder.BeginPhase(TestPhaseType.Test, test.Name, test.Description);
                context = await ExecuteActionsAsync(test.Actions, context, cancellationToken);
                recorder.EndPhase();
            }
        }

        if (definition.Teardown.Count > 0)
        {
            recorder.BeginPhase(TestPhaseType.Teardown);
            context = await ExecuteActionsAsync(definition.Teardown, context, cancellationToken);
            recorder.EndPhase();
        }

        return recorder.Build(definition.Metadata.Name, startTime, DateTimeOffset.UtcNow);
    }

    private async Task<TestScriptContext> ExecuteActionsAsync(
        IReadOnlyList<ActionExpression> actions,
        TestScriptContext context,
        CancellationToken cancellationToken)
    {
        foreach (var action in actions)
        {
            context = await action.AcceptAsync(this, context, cancellationToken);
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
            var client = context.ClientRegistry.GetDestination(expression.Destination);
            var request = BuildRequest(expression, context, client);

            context = context.WithRequest(expression.RequestId, request);
            var response = await client.SendAsync(request, cancellationToken);
            context = context.WithResponse(expression.ResponseId, response);

            sw.Stop();
            context.Recorder.RecordOperationResult(expression.Label, expression.Description,
                new OperationOutcome(true, response.StatusCode, Duration: sw.Elapsed));
            return context;
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
        var passed = EvaluateAssertion(expression, context);
        var message = passed ? null : BuildAssertionMessage(expression, context);
        context.Recorder.RecordAssertionResult(expression.Label, expression.Description,
            new AssertionOutcome(passed, expression.WarningOnly, message));
        return ValueTask.FromResult(context);
    }

    private static FhirRequest BuildRequest(OperationExpression op, TestScriptContext context, IFhirClient client)
    {
        var method = op.Method ?? DeriveMethod(op.Type);
        var url = BuildUrl(op, context, client);

        JsonNode? body = null;
        if (op.SourceId is not null && context.Fixtures.TryGetValue(op.SourceId, out var fixture))
            body = fixture.DeepClone();

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (op.Accept is not null) headers["Accept"] = op.Accept;
        if (op.ContentType is not null) headers["Content-Type"] = op.ContentType;
        foreach (var h in op.Headers)
            headers[VariableResolver.Resolve(h.Field, context)] = VariableResolver.Resolve(h.Value, context);

        return new FhirRequest { Method = method, Url = url, Body = body, Headers = headers };
    }

    private static string BuildUrl(OperationExpression op, TestScriptContext context, IFhirClient client)
    {
        if (op.Url is not null)
            return VariableResolver.Resolve(op.Url, context);

        var baseUrl = client.BaseUrl;
        var resource = op.Resource ?? string.Empty;
        var parameters = VariableResolver.ResolveIfNotNull(op.Params, context) ?? string.Empty;

        return $"{baseUrl}/{resource}{parameters}";
    }

    private static HttpMethod DeriveMethod(string operationType) => operationType switch
    {
        "create" => HttpMethod.Post,
        "read" or "vread" or "search" or "history" => HttpMethod.Get,
        "update" => HttpMethod.Put,
        "patch" => HttpMethod.Patch,
        "delete" => HttpMethod.Delete,
        _ => HttpMethod.Get
    };

    private static bool EvaluateAssertion(AssertExpression assertion, TestScriptContext context)
    {
        var response = assertion.Direction == AssertDirection.Response
            ? context.LastResponse
            : null;

        if (assertion.Response is not null && response is not null)
            return MatchesResponseCode(assertion.Response, response.StatusCode);

        if (assertion.ResponseCode is not null && response is not null)
            return response.StatusCode.ToString() == assertion.ResponseCode;

        if (assertion.Resource is not null && response?.Body is not null)
            return response.Body["resourceType"]?.GetValue<string>() == assertion.Resource;

        if (assertion.HeaderField is not null && response is not null)
        {
            var headerValue = response.Headers.GetValueOrDefault(assertion.HeaderField);
            return EvaluateWithOperator(headerValue, assertion.Value, assertion.Operator);
        }

        // FHIRPath expression assertions are not yet implemented (Phase 6)
        if (assertion.Expression is not null)
            return false;

        // Unknown assertion type — fail-closed
        return false;
    }

    private static bool EvaluateWithOperator(string? actual, string? expected, AssertOperator? op)
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
            null => actual is not null,
            _ => true
        };
    }

    private static string BuildAssertionMessage(AssertExpression assertion, TestScriptContext context)
    {
        var response = context.LastResponse;
        if (assertion.Response is not null && response is not null)
            return $"Expected response '{assertion.Response}' but got status {response.StatusCode}";
        if (assertion.ResponseCode is not null && response is not null)
            return $"Expected responseCode '{assertion.ResponseCode}' but got {response.StatusCode}";
        if (assertion.Resource is not null && response?.Body is not null)
            return $"Expected resource type '{assertion.Resource}' but got '{response.Body["resourceType"]?.GetValue<string>()}'";
        return "Assertion failed";
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
