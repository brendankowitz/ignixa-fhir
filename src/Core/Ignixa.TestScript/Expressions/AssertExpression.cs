using Ignixa.TestScript.Evaluation;

namespace Ignixa.TestScript.Expressions;

public sealed record AssertExpression : ActionExpression
{
    public required AssertCriteria Criteria { get; init; }
    public bool WarningOnly { get; init; }
    public AssertDirection Direction { get; init; } = AssertDirection.Response;
    public string? SourceId { get; init; }

    public override ValueTask<TestScriptContext> AcceptAsync(
        ITestScriptActionVisitor visitor,
        TestScriptContext context,
        CancellationToken cancellationToken)
        => visitor.VisitAssertAsync(this, context, cancellationToken);
}
