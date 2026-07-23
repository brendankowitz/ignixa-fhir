using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Locust.Diagnostics;
using Ignixa.TestScript.Locust.Ir;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Locust.Compilation;

/// <summary>
/// Deterministically lowers a parsed <see cref="TestScriptDefinition"/> into the versioned Locust
/// intermediate representation, preserving the exact operation, variable-extraction, and assertion
/// semantics honored by the Ignixa TestScript evaluator.
/// </summary>
public sealed class LocustIrCompiler
{
    private const string MetricDiagnosticCode = "LOCUST_METRIC";

    /// <summary>
    /// Compiles <paramref name="definition"/> into a <see cref="LocustIrDocument"/>.
    /// </summary>
    /// <param name="definition">The parsed TestScript definition to compile.</param>
    /// <param name="options">Options controlling the compilation.</param>
    /// <param name="cancellationToken">A token used to observe cancellation requests.</param>
    /// <returns>
    /// The compilation result. <see cref="LocustCompilationResult.Document"/> is <see langword="null"/>
    /// when <see cref="LocustSupportAnalyzer.Analyze"/> reports any error-severity diagnostic; no
    /// lowering is attempted in that case.
    /// </returns>
    public Task<LocustCompilationResult> CompileAsync(
        TestScriptDefinition definition,
        LocustCompilerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Source);
        cancellationToken.ThrowIfCancellationRequested();

        List<LocustDiagnostic> diagnostics = [.. LocustSupportAnalyzer.Analyze(definition, options.Source)];

        if (diagnostics.Exists(d => d.Severity == LocustDiagnosticSeverity.Error))
        {
            return Task.FromResult(new LocustCompilationResult(null, diagnostics));
        }

        List<LocustIrAction> setup = new(definition.Setup.Count);
        for (int i = 0; i < definition.Setup.Count; i++)
        {
            setup.Add(LowerAction(definition.Setup[i], $"setup.{i}"));
        }

        List<LocustIrTest> tests = new(definition.Tests.Count);
        for (int ti = 0; ti < definition.Tests.Count; ti++)
        {
            TestPhaseDefinition test = definition.Tests[ti];
            List<LocustIrAction> actions = new(test.Actions.Count);
            for (int ai = 0; ai < test.Actions.Count; ai++)
            {
                actions.Add(LowerAction(test.Actions[ai], $"test.{ti}.action.{ai}"));
            }

            tests.Add(new LocustIrTest
            {
                Id = $"test.{ti}",
                Name = test.Name,
                Description = test.Description,
                Actions = actions
            });
        }

        List<LocustIrOperation> teardown = new(definition.Teardown.Count);
        for (int i = 0; i < definition.Teardown.Count; i++)
        {
            teardown.Add(LowerOperation(definition.Teardown[i], $"teardown.{i}"));
        }

        LocustIrDocument document = new()
        {
            Metadata = new LocustIrMetadata(definition.Metadata.Name, options.Source, options.FhirVersion),
            Variables = [.. definition.Variables.Select(LowerVariable)],
            Setup = setup,
            Tests = tests,
            Teardown = teardown
        };

        AppendMetricDiagnostics(
            definition.Setup,
            options.Source,
            $"{options.Source}:setup:action:",
            "setup.",
            diagnostics);

        for (int ti = 0; ti < definition.Tests.Count; ti++)
        {
            TestPhaseDefinition test = definition.Tests[ti];
            AppendMetricDiagnostics(
                test.Actions,
                options.Source,
                $"{options.Source}:test:{test.Name}:action:",
                $"test.{ti}.action.",
                diagnostics);
        }

        AppendMetricDiagnostics(
            definition.Teardown,
            options.Source,
            $"{options.Source}:teardown:action:",
            "teardown.",
            diagnostics);

        return Task.FromResult(new LocustCompilationResult(document, diagnostics));
    }

    /// <summary>
    /// Appends a <see cref="LocustDiagnosticSeverity.Info"/> metric-mapping diagnostic for every
    /// action in <paramref name="actions"/> that will emit an independent Locust event: every
    /// operation, every ungrouped assertion, and only the first member of each
    /// <see cref="AssertExpression.AnyOfGroupId"/> group within this action list.
    /// </summary>
    private static void AppendMetricDiagnostics(
        IReadOnlyList<ActionExpression> actions,
        string metricSource,
        string diagnosticSourcePrefix,
        string actionIdPrefix,
        List<LocustDiagnostic> diagnostics)
    {
        HashSet<string> seenGroups = [];
        for (int i = 0; i < actions.Count; i++)
        {
            bool emitsMetric = actions[i] switch
            {
                AssertExpression { AnyOfGroupId: { } groupId } => seenGroups.Add(groupId),
                _ => true
            };

            if (!emitsMetric)
            {
                continue;
            }

            string actionId = $"{actionIdPrefix}{i}";
            diagnostics.Add(new LocustDiagnostic(
                MetricDiagnosticCode,
                LocustDiagnosticSeverity.Info,
                $"{diagnosticSourcePrefix}{i}",
                $"Metric '{metricSource}::{actionId}'"));
        }
    }

    private static LocustIrAction LowerAction(ActionExpression action, string id) => action switch
    {
        OperationExpression operation => LowerOperation(operation, id),
        AssertExpression assert => LowerAssertion(assert, id),
        _ => throw new InvalidOperationException($"Unsupported TestScript action type '{action.GetType().Name}'.")
    };

    private static LocustIrOperation LowerOperation(OperationExpression operation, string id) => new()
    {
        Id = id,
        Label = operation.Label,
        Description = operation.Description,
        Type = operation.Type,
        Method = DeriveMethod(operation),
        Resource = operation.Resource,
        Url = operation.Url,
        Params = operation.Params,
        Accept = operation.Accept,
        ContentType = operation.ContentType,
        SourceId = operation.SourceId,
        ResponseId = operation.ResponseId,
        RequestId = operation.RequestId,
        EncodeRequestUrl = operation.EncodeRequestUrl,
        Headers = [.. operation.Headers.Select(h => new LocustIrHeader(h.Field, h.Value))],
        WaitFor = operation.WaitFor is { } waitFor
            ? new LocustIrWaitFor(waitFor.PollingStatusCode, waitFor.MaxAttempts, waitFor.IntervalMs)
            : null
    };

    /// <summary>
    /// Derives the HTTP method for an operation exactly as <c>TestScriptEvaluator.BuildRequest</c> does:
    /// an explicit <see cref="OperationExpression.Method"/> always wins, otherwise the method is
    /// derived from <see cref="OperationExpression.Type"/>.
    /// </summary>
    private static string DeriveMethod(OperationExpression operation) =>
        (operation.Method ?? operation.Type switch
        {
            "create" => HttpMethod.Post,
            "read" or "vread" or "search" or "history" or "capabilities" or "conforms"
                => HttpMethod.Get,
            "update" or "updateCreate" => HttpMethod.Put,
            "patch" => HttpMethod.Patch,
            "delete" => HttpMethod.Delete,
            _ => HttpMethod.Post
        }).Method;

    private static LocustIrAssertion LowerAssertion(AssertExpression assert, string id) => new()
    {
        Id = id,
        Label = assert.Label,
        Description = assert.Description,
        Criteria = LowerCriteria(assert.Criteria),
        WarningOnly = assert.WarningOnly,
        Direction = assert.Direction == AssertDirection.Request ? "request" : "response",
        SourceId = assert.SourceId,
        AnyOfGroupId = assert.AnyOfGroupId,
        WhenResponseSourceId = assert.WhenResponseStatus?.SourceId,
        WhenResponseStatuses = assert.WhenResponseStatus is { } condition ? [.. condition.Statuses] : []
    };

    private static LocustIrAssertionCriteria LowerCriteria(AssertCriteria criteria) => criteria switch
    {
        ResponseStatusCriteria c => new LocustIrAssertionCriteria
        {
            Kind = LocustIrAssertionKind.ResponseStatus,
            Value = c.Status
        },
        ResponseCodeCriteria c => new LocustIrAssertionCriteria
        {
            Kind = LocustIrAssertionKind.ResponseCode,
            Value = c.Code
        },
        ContentTypeCriteria c => new LocustIrAssertionCriteria
        {
            Kind = LocustIrAssertionKind.ContentType,
            Value = c.ContentType
        },
        ResourceTypeCriteria c => new LocustIrAssertionCriteria
        {
            Kind = LocustIrAssertionKind.ResourceType,
            Value = c.ResourceType
        },
        HeaderCriteria c => new LocustIrAssertionCriteria
        {
            Kind = LocustIrAssertionKind.Header,
            Field = c.Field,
            Value = c.Value,
            Operator = c.Operator?.ToString()
        },
        FhirPathCriteria c => new LocustIrAssertionCriteria
        {
            Kind = LocustIrAssertionKind.FhirPath,
            Expression = c.Expression
        },
        FhirPathValueCriteria c => new LocustIrAssertionCriteria
        {
            Kind = LocustIrAssertionKind.FhirPathValue,
            Expression = c.Expression,
            Value = c.Value,
            Operator = c.Operator.ToString()
        },
        RequestMethodCriteria c => new LocustIrAssertionCriteria
        {
            Kind = LocustIrAssertionKind.RequestMethod,
            Value = c.Method
        },
        RequestUrlCriteria c => new LocustIrAssertionCriteria
        {
            Kind = LocustIrAssertionKind.RequestUrl,
            Value = c.Url,
            Operator = c.Operator?.ToString()
        },
        _ => throw new InvalidOperationException($"Unsupported assertion criteria type '{criteria.GetType().Name}'.")
    };

    private static LocustIrVariable LowerVariable(VariableDefinition variable)
    {
        (LocustIrVariableExtractionKind kind, string? selector) = LowerExtraction(variable.Extraction);
        return new LocustIrVariable(variable.Name, variable.DefaultValue, variable.SourceId, kind, selector);
    }

    private static (LocustIrVariableExtractionKind Kind, string? Selector) LowerExtraction(VariableExtraction? extraction) =>
        extraction switch
        {
            null => (LocustIrVariableExtractionKind.None, null),
            HeaderExtraction h => (LocustIrVariableExtractionKind.Header, h.Field),
            PathExtraction p => (LocustIrVariableExtractionKind.Path, p.Path),
            ExpressionExtraction e => (LocustIrVariableExtractionKind.FhirPath, e.Expression),
            _ => throw new InvalidOperationException(
                $"Unsupported variable extraction type '{extraction.GetType().Name}'.")
        };
}
