using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

public class Issue406RegressionTests
{
    [Theory]
    [InlineData("Patient.birthDate.type().name")]
    [InlineData("Patient.birthDate.type().namespace")]
    [InlineData("Patient.birthDate.type().baseType")]
    public void GivenTypeReflectionMember_WhenAnalyzed_ThenInfersString(string expression)
    {
        var analyzer = new FhirPathAnalyzer(FhirVersion.R5.GetSchemaProvider());

        var result = analyzer.Analyze(expression, "Patient");

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

    [Theory]
    [InlineData("%vs-administrative-gender")]
    [InlineData("%ext-patient-birthTime")]
    public void GivenBareSpecificationConstant_WhenAnalyzed_ThenReportsItUndefinedLikeTheEngine(string expression)
    {
        // #438 review. The analyzer recognises the vs-/ext- families by shape so it does not have to
        // enumerate several hundred names, but it must recognise them on the same terms the engine resolves
        // them on - only the backtick spelling, per the FHIR profile of FHIRPath and HAPI. An analyzer that
        // accepted the bare spelling would report clean and then let evaluation throw, which is worse than
        // either being consistently strict or consistently lenient. The name in the message is the whole
        // hyphenated one, which is what #438's lexer fix buys.
        var analyzer = new FhirPathAnalyzer(FhirVersion.R5.GetSchemaProvider());

        var result = analyzer.Analyze(expression, "Patient");

        result.Errors.ShouldContain(message => message.Contains($"'{expression}' not found", StringComparison.Ordinal));
    }
}
