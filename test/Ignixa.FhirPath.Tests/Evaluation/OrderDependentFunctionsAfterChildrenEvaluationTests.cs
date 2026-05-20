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

    [Fact]
    public void GivenSkipAfterChildren_WhenEvaluating_ThenThrowsInvalidOperationException()
    {
        var patient = ResourceJsonNode.Parse("""
        {
          "resourceType": "Patient",
          "id": "p1",
          "active": true,
          "gender": "male"
        }
        """).ToElement(FhirVersion.R4.GetSchemaProvider());
        var expression = _parser.Parse("Patient.children().skip(1)");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(patient, expression, new EvaluationContext()).ToList());

        Assert.Contains("skip()", exception.Message, StringComparison.Ordinal);
        Assert.Contains("children()", exception.Message, StringComparison.Ordinal);
    }
}
