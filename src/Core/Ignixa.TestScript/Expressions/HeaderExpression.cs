namespace Ignixa.TestScript.Expressions;

public sealed record HeaderExpression
{
    public required string Field { get; init; }
    public required string Value { get; init; }
}
