using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;

namespace Ignixa.TestScript.Locust.Tests.Contracts;

/// <summary>
/// Reference-side half of the shared FHIRPath contract (Task 9). Every case in
/// <c>Contracts/fhirpath-cases.json</c> is evaluated here with the real Ignixa FhirPath engine,
/// using the exact boolean/scalar adapters the TestScript evaluator uses:
/// <list type="bullet">
///   <item>boolean: <c>element.IsTrue(expression)</c> - the predicate adapter used for
///   <c>requiresCapability</c> and FHIRPath assertion criteria.</item>
///   <item>scalar: <c>element.Select(expression).AsString()</c> - the single-value adapter used for
///   FHIRPath variable extraction and <c>compareToSourceExpression</c> value assertions.</item>
/// </list>
/// This suite pins that the contract's <c>expected</c> values ARE Ignixa's real behavior. The Python
/// <c>test_fhirpath_contract.py</c> then holds the Task 9 runtime adapter to the identical values, so
/// the two engines cannot silently diverge. Expected values are never adjusted to fhirpathpy.
/// </summary>
public class FhirPathContractTests
{
    private static readonly IFhirSchemaProvider s_schema = FhirVersion.R4.GetSchemaProvider();

    public static IEnumerable<object?[]> Cases()
    {
        foreach (ContractCase c in ContractCaseLoader.Load())
        {
            // shape+expected are flattened to primitives so xUnit can serialize the theory data.
            yield return c.Shape == "boolean"
                ? [c.Name, c.Expression, c.Shape, c.ExpectedBoolean, null, c.ResourceJson]
                : [c.Name, c.Expression, c.Shape, false, c.ExpectedScalar, c.ResourceJson];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GivenSharedContractCase_WhenEvaluatedWithIgnixa_ThenMatchesExpected(
        string name,
        string expression,
        string shape,
        bool expectedBoolean,
        string? expectedScalar,
        string resourceJson)
    {
        ResourceJsonNode resource = JsonSourceNodeFactory.Parse(resourceJson);
        IElement element = resource.ToElement(s_schema);

        if (shape == "boolean")
        {
            bool actual = element.IsTrue(expression);
            actual.ShouldBe(expectedBoolean, $"boolean case '{name}': {expression}");
        }
        else if (shape == "scalar")
        {
            string? actual = element.Select(expression).AsString();
            actual.ShouldBe(expectedScalar, $"scalar case '{name}': {expression}");
        }
        else
        {
            throw new InvalidOperationException($"Unknown contract shape '{shape}' for case '{name}'.");
        }
    }

    [Fact]
    public void GivenContract_WhenLoaded_ThenSeedCasesArePresentInDeclaredOrder()
    {
        List<ContractCase> cases = [.. ContractCaseLoader.Load()];

        cases.Count.ShouldBeGreaterThan(3);
        cases[0].Expression.ShouldBe("Patient.id.exists()");
        cases[0].Shape.ShouldBe("boolean");
        cases[0].ExpectedBoolean.ShouldBeTrue();

        cases[1].Expression.ShouldBe("Patient.active");
        cases[1].Shape.ShouldBe("scalar");
        cases[1].ExpectedScalar.ShouldBe("true");

        cases[2].Expression.ShouldBe("Patient.id");
        cases[2].Shape.ShouldBe("scalar");
        cases[2].ExpectedScalar.ShouldBeNull();
    }
}
