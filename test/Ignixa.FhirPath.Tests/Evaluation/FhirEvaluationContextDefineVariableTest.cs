using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class FhirEvaluationContextDefineVariableTest
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Fact]
    public void GivenFhirEvaluationContext_WhenDefineVariableInWhereClause_ThenVariableAccessible()
    {
        var expr = _parser.Parse("(1 | 2 | 3 | 4).defineVariable('threshold', 2).where($this > %threshold).select($this * 10)");
        var root = new TestElement(0, "integer");

        // Use FhirEvaluationContext instead of base EvaluationContext
        var context = new FhirEvaluationContext();
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(30, result[0].Value);
        Assert.Equal(40, result[1].Value);
    }

    private class TestElement(object value, string type) : IElement
    {
        public string Name => string.Empty;
        public string InstanceType => type;
        public object Value => value;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => [];

        public T? Meta<T>() where T : class => null;
    }
}
