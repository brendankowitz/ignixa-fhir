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
        // The analyzer recognises the vs-/ext- families by shape rather than enumerating several hundred
        // names, but has to recognise them on the same terms the engine resolves them on - the backtick
        // spelling only. An analyzer that accepted the bare spelling would report clean and then let
        // evaluation throw. The name in the message is the whole hyphenated one, which #438's lexer fix
        // is what buys.
        var analyzer = new FhirPathAnalyzer(FhirVersion.R5.GetSchemaProvider());

        var result = analyzer.Analyze(expression, "Patient");

        result.Errors.ShouldContain(message => message.Contains($"'{expression}' not found", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("%`vs-`", "vs-")]
    [InlineData("%`ext-`", "ext-")]
    public void GivenDelimitedEmptySuffixConstant_WhenAnalyzed_ThenReportsItUndefinedLikeTheEngine(
        string expression, string expectedName)
    {
        // GetStandardConstant requires a non-empty suffix, so %`vs-` and %`ext-` are not
        // ValueSet/StructureDefinition references at evaluation - they throw "undefined environment
        // variable: vs-". ResolveVariable used to match on prefix alone and report them clean: analyzer
        // silent, engine throwing. Both now ask StandardConstantFamilies.
        var analyzer = new FhirPathAnalyzer(FhirVersion.R5.GetSchemaProvider());

        var result = analyzer.Analyze(expression, "Patient");

        result.Errors.ShouldContain(message => message.Contains($"'%{expectedName}' not found", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("vs-administrative-gender")]
    [InlineData("ext-patient-birthTime")]
    public void GivenTheOneArgumentOverload_WhenResolvingAPrefixedConstant_ThenItAnswersAsItDidBeforeTheFlagExisted(
        string name)
    {
        // ResolveVariable(string) is a real overload, not an optional parameter: an optional parameter
        // would remove that signature from the assembly's metadata, and a consumer compiled against the
        // published package would throw MissingMethodException without recompiling. It forwards
        // isDelimited: true, preserving what a one-argument call returned before the flag existed.
        //
        // The claim is asserted against metadata rather than against a call, because overload-versus-
        // default is invisible at source level: collapsing the two into one optional parameter was
        // reproduced and left the calls below passing 2/2.
        typeof(AnalysisContext)
            .GetMethod(nameof(AnalysisContext.ResolveVariable), [typeof(string)])
            .ShouldNotBeNull(
                "AnalysisContext.ResolveVariable(string) is no longer in the assembly's metadata. A "
                + "consumer compiled against the published package binds to that exact signature, so it "
                + "would throw MissingMethodException without being recompiled. An optional parameter is "
                + "source-compatible but not binary-compatible; keep the one-argument overload.");

        var context = AnalysisContext.Create(FhirVersion.R5.GetSchemaProvider(), "Patient");

        var byNameAlone = context.ResolveVariable(name);
        byNameAlone.ShouldNotBeNull();
        byNameAlone.Types.ShouldHaveSingleItem().TypeName.ShouldBe("string");

        context.ResolveVariable(name, isDelimited: false).ShouldBeNull();
        context.ResolveVariable(name, isDelimited: true).ShouldNotBeNull();
    }
}
