using Ignixa.TestScript.Evaluation;

namespace Ignixa.TestScript.Expressions;

public sealed record OperationExpression : ActionExpression
{
    public required string Type { get; init; }
    public string? Resource { get; init; }
    public string? Url { get; init; }
    public string? Params { get; init; }
    public HttpMethod? Method { get; init; }
    public string? Accept { get; init; }
    public string? ContentType { get; init; }
    public string? SourceId { get; init; }

    /// <summary>
    /// Parsed from the TestScript but not yet implemented by the evaluator. The fixture/response
    /// targeted by a write operation is currently inferred from <see cref="SourceId"/> and the
    /// response history rather than this property.
    /// </summary>
    public string? TargetId { get; init; }
    public string? ResponseId { get; init; }
    public string? RequestId { get; init; }
    public int? Destination { get; init; }
    public int? Origin { get; init; }
    public IReadOnlyList<HeaderExpression> Headers { get; init; } = [];
    public bool EncodeRequestUrl { get; init; } = true;

    public override ValueTask<TestScriptContext> AcceptAsync(
        ITestScriptActionVisitor visitor,
        TestScriptContext context,
        CancellationToken cancellationToken)
        => visitor.VisitOperationAsync(this, context, cancellationToken);
}
