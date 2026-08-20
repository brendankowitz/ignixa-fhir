using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

public class Issue406RegressionTests
{
    [Fact]
    public void GivenTypeReflectionMember_WhenAnalyzed_ThenInfersString()
    {
        var analyzer = new FhirPathAnalyzer(FhirVersion.R5.GetSchemaProvider());

        var result = analyzer.Analyze("Patient.birthDate.type().name", "Patient");

        result.Errors.ShouldBeEmpty();
        result.InferredTypes.Types.ShouldHaveSingleItem().TypeName.ShouldBe("string");
    }

    [Fact]
    public void GivenDescendantsMemberNavigation_WhenAnalyzed_ThenReportsIndeterminateWithoutErrors()
    {
        var analyzer = new FhirPathAnalyzer(FhirVersion.R5.GetSchemaProvider());

        var result = analyzer.Analyze("Patient.descendants().notKnownStatically", "Patient");

        result.Errors.ShouldBeEmpty();
        result.Warnings.ShouldContain(message =>
            message.Contains("descendants()", StringComparison.Ordinal) &&
            message.Contains("cannot be analysed", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(FhirVersion.R4, "ConceptMap.group.element.target.product.property", "ConceptMap")]
    [InlineData(FhirVersion.R5, "BodyStructure.excludedStructure.structure", "BodyStructure")]
    [InlineData(FhirVersion.R5, "Composition.section.section.text", "Composition")]
    public void GivenContentReferenceNavigation_WhenAnalyzed_ThenHasNoErrors(
        FhirVersion version,
        string expression,
        string rootType)
    {
        var analyzer = new FhirPathAnalyzer(version.GetSchemaProvider());

        var result = analyzer.Analyze(expression, rootType);

        result.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Specimen.container.device.resolve().location")]
    [InlineData("Specimen.container.device.resolve().owner")]
    public void GivenResolveResultMember_WhenAnalyzed_ThenReportsIndeterminateInsteadOfInvalid(string expression)
    {
        var analyzer = new FhirPathAnalyzer(FhirVersion.R6.GetSchemaProvider());

        var result = analyzer.Analyze(expression, "Specimen");

        result.Errors.ShouldBeEmpty();
        result.Warnings.ShouldContain(message =>
            message.Contains("resolve()", StringComparison.Ordinal) &&
            message.Contains("cannot be analysed", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("%`vs-administrative-gender`")]
    [InlineData("%`ext-patient-birthTime`")]
    public void GivenDelimitedSpecificationConstant_WhenAnalyzed_ThenHasNoErrors(string expression)
    {
        var analyzer = new FhirPathAnalyzer(FhirVersion.R5.GetSchemaProvider());

        var result = analyzer.Analyze(expression, "Patient");

        result.Errors.ShouldBeEmpty();
        result.InferredTypes.Types.ShouldHaveSingleItem().TypeName.ShouldBe("string");
    }
}
