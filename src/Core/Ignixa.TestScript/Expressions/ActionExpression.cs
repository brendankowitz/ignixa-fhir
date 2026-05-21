using Ignixa.TestScript.Evaluation;

namespace Ignixa.TestScript.Expressions;

public abstract record ActionExpression
{
    public string? Description { get; init; }
    public string? Label { get; init; }

    public abstract ValueTask<TestScriptContext> AcceptAsync(
        ITestScriptActionVisitor visitor,
        TestScriptContext context,
        CancellationToken cancellationToken);
}
