using System.Linq;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class OrderDependentFunctionsAfterChildrenEvaluationTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    private IElement Patient() => ResourceJsonNode.Parse("""
        {
          "resourceType": "Patient",
          "id": "p1",
          "active": true,
          "gender": "male",
          "name": [{"family": "Smith"}, {"family": "Jones"}]
        }
        """).ToElement(FhirVersion.R4.GetSchemaProvider());

    private IEnumerable<IElement> Evaluate(IElement element, string expression)
    {
        var parsed = _parser.Parse(expression);
        return _evaluator.Evaluate(element, parsed, new EvaluationContext());
    }

    [Theory]
    [InlineData("Patient.children().skip(1)")]
    [InlineData("Patient.children().take(2)")]
    [InlineData("Patient.children().tail()")]
    [InlineData("Patient.descendants().skip(1)")]
    [InlineData("Patient.descendants().take(1)")]
    [InlineData("Patient.descendants().tail()")]
    public void GivenPositionalFunctionAfterChildren_WhenEvaluating_ThenReturnsEmpty(string expr)
    {
        var result = Evaluate(Patient(), expr).ToList();
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("Patient.children().where(true).skip(1)")]
    [InlineData("Patient.children().ofType(HumanName).take(1)")]
    [InlineData("Patient.descendants().where(true).tail()")]
    public void GivenPositionalFunctionAfterChildrenIndirect_WhenEvaluating_ThenReturnsEmpty(string expr)
    {
        var result = Evaluate(Patient(), expr).ToList();
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("Patient.children().first()")]
    [InlineData("Patient.children().last()")]
    [InlineData("Patient.descendants().first()")]
    [InlineData("Patient.descendants().last()")]
    public void GivenExistentialFunctionAfterChildren_WhenEvaluating_ThenReturnsResult(string expr)
    {
        var result = Evaluate(Patient(), expr).ToList();
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GivenIndexerAfterChildren_WhenEvaluating_ThenReturnsEmpty()
    {
        var result = Evaluate(Patient(), "Patient.children()[0]").ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void GivenIndexerAfterChildrenIndirect_WhenEvaluating_ThenReturnsEmpty()
    {
        var result = Evaluate(Patient(), "Patient.children().where(true)[0]").ToList();
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("Patient.children().sort().skip(1)")]
    [InlineData("Patient.children().sort().take(1)")]
    [InlineData("Patient.children().sort()[0]")]
    public void GivenSortBreaksChain_WhenEvaluating_ThenReturnsResult(string expr)
    {
        var result = Evaluate(Patient(), expr).ToList();
        Assert.NotEmpty(result);
    }

    [Theory]
    [InlineData("Patient.name.skip(1)")]
    [InlineData("Patient.name.first()")]
    [InlineData("Patient.name[0]")]
    public void GivenOrderedPathAccess_WhenEvaluating_ThenReturnsResult(string expr)
    {
        var result = Evaluate(Patient(), expr).ToList();
        Assert.NotEmpty(result);
    }
}
