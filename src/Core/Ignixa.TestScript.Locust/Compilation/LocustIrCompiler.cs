using Ignixa.TestScript.Evaluation;
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
    /// when <see cref="LocustSupportAnalyzer.Analyze"/> reports any error-severity diagnostic, or when
    /// any fixture fails to compile; no partial document is ever produced in either case.
    /// </returns>
    public async Task<LocustCompilationResult> CompileAsync(
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
            return new LocustCompilationResult(null, diagnostics);
        }

        var fixtureCompiler = new LocustFixtureCompiler(options.Schema);
        List<LocustIrFixture> fixtures = new(definition.Fixtures.Count);
        var hasFixtureError = false;
        foreach (FixtureDefinition fixture in definition.Fixtures)
        {
            (LocustIrFixture? compiledFixture, LocustDiagnostic? fixtureDiagnostic) = await fixtureCompiler.CompileAsync(
                fixture,
                options.FixtureVariants,
                $"{options.Source}:fixture:{fixture.Id}",
                cancellationToken);

            if (fixtureDiagnostic is not null)
            {
                diagnostics.Add(fixtureDiagnostic);
                hasFixtureError = true;
                continue;
            }

            fixtures.Add(compiledFixture!);
        }

        if (hasFixtureError)
        {
            return new LocustCompilationResult(null, diagnostics);
        }

        List<LocustIrAction> setup = new(definition.Setup.Count);
        for (int i = 0; i < definition.Setup.Count; i++)
        {
            setup.Add(LowerAction(definition.Setup[i], $"setup.{i}"));
        }

        List<LocustIrTest> tests = new(definition.Tests.Count);
        List<(TestPhaseDefinition Definition, string Id, string SourceSuffix)> testMetricSources = [];
        for (int ti = 0; ti < definition.Tests.Count; ti++)
        {
            TestPhaseDefinition test = definition.Tests[ti];

            if (test.FhirVersions.Count > 0
                && !TestScriptVersionCompatibility.IsCompatible(test.FhirVersions, options.FhirVersion))
            {
                continue;
            }

            if (test.Parameters is null)
            {
                string testId = $"test.{ti}";
                List<LocustIrAction> actions = LowerActions(test.Actions, testId);

                tests.Add(new LocustIrTest
                {
                    Id = testId,
                    Name = test.Name,
                    Description = test.Description,
                    RequiresCapability = test.RequiresCapability,
                    Actions = actions
                });
                testMetricSources.Add((test, testId, string.Empty));
            }
            else
            {
                ParametrizeDefinition parameters = test.Parameters;
                for (int vi = 0; vi < parameters.Values.Count; vi++)
                {
                    string value = parameters.Values[vi];
                    string testId = $"test.{ti}.param.{vi}";
                    List<LocustIrAction> actions = LowerActions(test.Actions, testId);

                    tests.Add(new LocustIrTest
                    {
                        Id = testId,
                        Name = $"{test.Name} [{value}]",
                        Description = test.Description,
                        RequiresCapability = test.RequiresCapability,
                        DiscardContextAfterExecution = true,
                        InitialVariables = new Dictionary<string, string> { [parameters.VariableName] = value },
                        Actions = actions
                    });
                    // Every parametrize expansion shares the same underlying TestPhaseDefinition (and
                    // therefore the same Name), so the diagnostic Source must be disambiguated by value
                    // index -- otherwise distinct compiled actions across expansions would collide on
                    // an identical (Code, Severity, Source) tuple, differing only by Message text.
                    testMetricSources.Add((test, testId, $":param:{vi}"));
                }
            }
        }

        List<LocustIrOperation> teardown = new(definition.Teardown.Count);
        for (int i = 0; i < definition.Teardown.Count; i++)
        {
            teardown.Add(LowerOperation(definition.Teardown[i], $"teardown.{i}"));
        }

        LocustIrDocument document = new()
        {
            Metadata = new LocustIrMetadata(definition.Metadata.Name, options.Source, options.FhirVersion),
            RequiresCapability = definition.Metadata.RequiresCapability,
            Fixtures = fixtures,
            Variables = [.. definition.Variables.Select(LowerVariable)],
            Setup = setup,
            Tests = tests,
            Teardown = teardown
        };

        AppendFixtureMetricDiagnostics(fixtures, options.Source, diagnostics);

        AppendMetricDiagnostics(
            definition.Setup,
            options.Source,
            $"{options.Source}:setup:action:",
            "setup.",
            diagnostics);

        foreach ((TestPhaseDefinition test, string testId, string sourceSuffix) in testMetricSources)
        {
            AppendMetricDiagnostics(
                test.Actions,
                options.Source,
                $"{options.Source}:test:{test.Name}{sourceSuffix}:action:",
                $"{testId}.action.",
                diagnostics);
        }

        AppendMetricDiagnostics(
            definition.Teardown,
            options.Source,
            $"{options.Source}:teardown:action:",
            "teardown.",
            diagnostics);

        return new LocustCompilationResult(document, diagnostics);
    }

    private static List<LocustIrAction> LowerActions(IReadOnlyList<ActionExpression> actions, string idPrefix)
    {
        List<LocustIrAction> result = new(actions.Count);
        for (int i = 0; i < actions.Count; i++)
        {
            result.Add(LowerAction(actions[i], $"{idPrefix}.action.{i}"));
        }

        return result;
    }

    /// <summary>
    /// Appends a <see cref="LocustDiagnosticSeverity.Info"/> metric-mapping diagnostic for every
    /// enabled fixture lifecycle operation (<see cref="LocustIrFixture.Autocreate"/> and
    /// <see cref="LocustIrFixture.Autodelete"/>), in fixture-definition order. Only successfully
    /// compiled fixtures reach this point, since any fixture-compilation failure short-circuits the
    /// whole compilation before this method is ever called.
    /// </summary>
    private static void AppendFixtureMetricDiagnostics(
        IReadOnlyList<LocustIrFixture> fixtures,
        string metricSource,
        List<LocustDiagnostic> diagnostics)
    {
        foreach (LocustIrFixture fixture in fixtures)
        {
            if (fixture.Autocreate)
            {
                string id = $"fixture.{fixture.Id}.autocreate";
                diagnostics.Add(new LocustDiagnostic(
                    MetricDiagnosticCode,
                    LocustDiagnosticSeverity.Info,
                    $"{metricSource}:fixture:{fixture.Id}:autocreate",
                    $"Metric '{metricSource}::{id}'"));
            }

            if (fixture.Autodelete)
            {
                string id = $"fixture.{fixture.Id}.autodelete";
                diagnostics.Add(new LocustDiagnostic(
                    MetricDiagnosticCode,
                    LocustDiagnosticSeverity.Info,
                    $"{metricSource}:fixture:{fixture.Id}:autodelete",
                    $"Metric '{metricSource}::{id}'"));
            }
        }
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
