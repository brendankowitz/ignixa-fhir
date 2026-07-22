using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Locust.Diagnostics;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Locust.Compilation;

/// <summary>
/// Read-only analyzer that walks a parsed <see cref="TestScriptDefinition"/> and reports the
/// exact set of semantics that Ignixa's Locust compiler/runtime does not support or only
/// partially honors. This analyzer performs no lowering and produces no side effects; it only
/// classifies the definition's actions and assertions.
/// </summary>
public static class LocustSupportAnalyzer
{
    public static IReadOnlyList<LocustDiagnostic> Analyze(TestScriptDefinition definition, string source)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        List<LocustDiagnostic> diagnostics = [];

        if (definition.Profiles.Count > 0)
        {
            diagnostics.Add(new LocustDiagnostic(
                "LOCUST005",
                LocustDiagnosticSeverity.Warning,
                source,
                "TestScript profile references were parsed but the Locust evaluator does not consume them."));
        }

        for (int i = 0; i < definition.Setup.Count; i++)
        {
            AnalyzeAction(definition.Setup[i], $"{source}:setup:action:{i}", diagnostics);
        }

        foreach (TestPhaseDefinition test in definition.Tests)
        {
            for (int i = 0; i < test.Actions.Count; i++)
            {
                AnalyzeAction(test.Actions[i], $"{source}:test:{test.Name}:action:{i}", diagnostics);
            }
        }

        for (int i = 0; i < definition.Teardown.Count; i++)
        {
            AnalyzeAction(definition.Teardown[i], $"{source}:teardown:action:{i}", diagnostics);
        }

        return diagnostics;
    }

    private static void AnalyzeAction(ActionExpression action, string actionSource, List<LocustDiagnostic> diagnostics)
    {
        switch (action)
        {
            case OperationExpression operation:
                AnalyzeOperation(operation, actionSource, diagnostics);
                break;
            case AssertExpression assert:
                AnalyzeAssert(assert, actionSource, diagnostics);
                break;
            default:
                diagnostics.Add(new LocustDiagnostic(
                    "LOCUST006",
                    LocustDiagnosticSeverity.Error,
                    actionSource,
                    $"Unsupported TestScript action type '{action.GetType().Name}'."));
                break;
        }
    }

    private static void AnalyzeOperation(OperationExpression operation, string actionSource, List<LocustDiagnostic> diagnostics)
    {
        if (operation.Destination is > 1)
        {
            diagnostics.Add(new LocustDiagnostic(
                "LOCUST001",
                LocustDiagnosticSeverity.Error,
                actionSource,
                $"Operation destination '{operation.Destination}' is unsupported; only destination 1 is supported."));
        }

        if (operation.Origin is not null)
        {
            diagnostics.Add(new LocustDiagnostic(
                "LOCUST002",
                LocustDiagnosticSeverity.Error,
                actionSource,
                $"Operation origin '{operation.Origin}' is unsupported; origin execution is not implemented."));
        }

        if (operation.TargetId is not null)
        {
            diagnostics.Add(new LocustDiagnostic(
                "LOCUST003",
                LocustDiagnosticSeverity.Error,
                actionSource,
                $"Operation targetId '{operation.TargetId}' was parsed but is not implemented by Ignixa.TestScript."));
        }

        if (!operation.EncodeRequestUrl)
        {
            diagnostics.Add(new LocustDiagnostic(
                "LOCUST004",
                LocustDiagnosticSeverity.Warning,
                actionSource,
                "encodeRequestUrl=false is unsupported at runtime; Ignixa's encode-and-warn behavior is preserved."));
        }
    }

    private static void AnalyzeAssert(AssertExpression assert, string actionSource, List<LocustDiagnostic> diagnostics)
    {
        bool isAccepted = assert.Criteria switch
        {
            ResponseStatusCriteria => true,
            ResponseCodeCriteria => true,
            ContentTypeCriteria => true,
            ResourceTypeCriteria => true,
            HeaderCriteria => true,
            FhirPathCriteria => true,
            FhirPathValueCriteria => true,
            RequestMethodCriteria => true,
            RequestUrlCriteria => true,
            _ => false
        };

        if (!isAccepted)
        {
            diagnostics.Add(new LocustDiagnostic(
                "LOCUST006",
                LocustDiagnosticSeverity.Error,
                actionSource,
                $"Unsupported assertion criteria type '{assert.Criteria.GetType().Name}'."));
        }
    }
}
