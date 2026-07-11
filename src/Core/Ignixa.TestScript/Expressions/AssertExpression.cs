using Ignixa.TestScript.Evaluation;

namespace Ignixa.TestScript.Expressions;

public sealed record AssertExpression : ActionExpression
{
    public required AssertCriteria Criteria { get; init; }
    public bool WarningOnly { get; init; }
    public AssertDirection Direction { get; init; } = AssertDirection.Response;
    public string? SourceId { get; init; }

    /// <summary>
    /// Group identifier from the <c>http://ignixa.io/testscript/assertionAnyOfGroup</c> extension.
    /// Assertions sharing a non-null group id within one test form a strict OR group: at least one
    /// applicable member must pass, or the whole group hard-fails, regardless of each member's own
    /// <see cref="WarningOnly"/> flag (which is preserved for older-engine compatibility only).
    /// </summary>
    public string? AnyOfGroupId { get; init; }

    /// <summary>
    /// Parsed <c>http://ignixa.io/testscript/assertionWhenResponseStatus</c> extension. When set,
    /// this assertion only applies (is evaluated) when the referenced earlier response's status
    /// matches; otherwise it is skipped ("not applicable"), not failed.
    /// </summary>
    public ResponseStatusCondition? WhenResponseStatus { get; init; }

    public override ValueTask<TestScriptContext> AcceptAsync(
        ITestScriptActionVisitor visitor,
        TestScriptContext context,
        CancellationToken cancellationToken)
        => visitor.VisitAssertAsync(this, context, cancellationToken);
}
