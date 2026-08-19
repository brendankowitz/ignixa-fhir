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

    /// <summary>
    /// sort() re-establishes an order that children() removed, so a positional function after it is
    /// meaningful again.
    /// </summary>
    /// <remarks>
    /// These sort Patient.name.children() - two family strings - rather than Patient.children(), which
    /// mixes a boolean with an id and a code. Sorting that bag now signals an error, as FHIRPath 3.0
    /// requires for incompatible types; it only appeared to work because the old comparer wrapped
    /// CompareTo in a bare catch that answered "equal". The subject here is the ordered-chain analysis,
    /// not what sort() does with mixed types, so the collection is made homogeneous rather than the
    /// assertion weakened. GivenSortOverMixedTypes_WhenEvaluating_ThenSignalsError below pins the
    /// behaviour these cases used to rely on.
    /// </remarks>
    [Theory]
    [InlineData("Patient.name.children().sort().skip(1)")]
    [InlineData("Patient.name.children().sort().take(1)")]
    [InlineData("Patient.name.children().sort()[0]")]
    public void GivenSortBreaksChain_WhenEvaluating_ThenReturnsResult(string expr)
    {
        var result = Evaluate(Patient(), expr).ToList();
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GivenSortOverMixedTypes_WhenEvaluating_ThenSignalsError()
    {
        var patient = Patient();

        Assert.Throws<FhirPathEvaluationException>(
            () => Evaluate(patient, "Patient.children().sort()").ToList());
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
