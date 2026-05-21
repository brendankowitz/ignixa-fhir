using Ignixa.TestScript.Evaluation;

namespace Ignixa.TestScript.Expressions;

public sealed record AssertExpression : ActionExpression
{
    public string? Response { get; init; }
    public string? ResponseCode { get; init; }
    public string? ContentType { get; init; }
    public string? Expression { get; init; }
    public string? Path { get; init; }
    public string? Value { get; init; }
    public string? SourceId { get; init; }
    public string? CompareToSourceId { get; init; }
    public string? CompareToSourceExpression { get; init; }
    public string? CompareToSourcePath { get; init; }
    public string? ValidateProfileId { get; init; }
    public string? Resource { get; init; }
    public string? MinimumId { get; init; }
    public string? HeaderField { get; init; }
    public string? RequestMethod { get; init; }
    public string? RequestUrl { get; init; }
    public bool? NavigationLinks { get; init; }
    public AssertOperator? Operator { get; init; }
    public bool WarningOnly { get; init; }
    public AssertDirection Direction { get; init; } = AssertDirection.Response;

    public override ValueTask<TestScriptContext> AcceptAsync(
        ITestScriptActionVisitor visitor,
        TestScriptContext context,
        CancellationToken cancellationToken)
        => visitor.VisitAssertAsync(this, context, cancellationToken);
}
