namespace Ignixa.TestScript.Evaluation;

public interface ITestScriptActionVisitor
{
    ValueTask<TestScriptContext> VisitOperationAsync(
        Expressions.OperationExpression expression,
        TestScriptContext context,
        CancellationToken cancellationToken);

    ValueTask<TestScriptContext> VisitAssertAsync(
        Expressions.AssertExpression expression,
        TestScriptContext context,
        CancellationToken cancellationToken);
}
