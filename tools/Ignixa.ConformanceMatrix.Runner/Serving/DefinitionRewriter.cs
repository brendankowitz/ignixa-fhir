using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Model;

namespace Ignixa.ConformanceMatrix.Runner.Serving;

/// <summary>
/// Applies a <c>/run</c> request's <see cref="RunRequestOptions"/> to a parsed
/// <see cref="TestScriptDefinition"/> before evaluation. Every knob uses a <c>with</c> expression,
/// so a request that needs none of them (the "full" / all-defaults case) gets back the exact same
/// definition instance rather than an equivalent copy.
/// </summary>
internal static class DefinitionRewriter
{
    public static DefinitionRewriteResult Apply(TestScriptDefinition definition, RunRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(options);

        if (ValidateAssertions(options.Assertions) is { } assertionError)
            return DefinitionRewriteResult.Invalid(assertionError);

        var rewritten = definition;

        // Fixtures still run/autocreate even with setup actions skipped — only the setup action
        // list is emptied, matching the Runner API contract's "fixture-aware setup control".
        if (!options.RunSetup)
            rewritten = rewritten with { Setup = [] };

        if (!options.RunTeardown)
            rewritten = rewritten with { Teardown = [] };

        if (options.Assertions == "none")
        {
            rewritten = rewritten with
            {
                Tests = rewritten.Tests
                    .Select(test => test with { Actions = test.Actions.Where(a => a is OperationExpression).ToList() })
                    .ToList()
            };
        }

        return DefinitionRewriteResult.Valid(rewritten);
    }

    /// <summary>
    /// "status-only" is part of the Runner API contract but is a Phase 3 feature — the evaluator has
    /// no status-only assertion mode yet, so it is rejected explicitly rather than silently treated
    /// as "full" or "none".
    /// </summary>
    private static string? ValidateAssertions(string assertions) => assertions switch
    {
        "full" or "none" => null,
        "status-only" => "assertions=status-only arrives with Phase 3; use full or none",
        _ => $"Unknown assertions value '{assertions}'; expected 'full' or 'none'"
    };
}
