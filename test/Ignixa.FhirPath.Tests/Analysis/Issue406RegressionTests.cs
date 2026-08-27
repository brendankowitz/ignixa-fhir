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

    [Theory]
    [InlineData("%`vs-`", "vs-")]
    [InlineData("%`ext-`", "ext-")]
    public void GivenDelimitedEmptySuffixConstant_WhenAnalyzed_ThenReportsItUndefinedLikeTheEngine(
        string expression, string expectedName)
    {
        // PR #442 final review (F4). GetStandardConstant requires a non-empty suffix - name.Length > 3 for
        // "vs-", > 4 for "ext-" - so %`vs-` and %`ext-` are not ValueSet/StructureDefinition references at
        // evaluation; they throw "undefined environment variable: vs-"/"ext-". Before this fix
        // ResolveVariable matched on prefix alone and reported these clean, which is exactly the mismatch
        // this branch exists to remove: the analyzer silent, the engine throwing. Both now ask
        // StandardConstantFamilies, which requires the same non-empty suffix.
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
        // PR #442 standards review, P1/P2. ResolveVariable gained an isDelimited parameter. Adding it as an
        // optional parameter rather than an overload would have removed ResolveVariable(string) from the
        // assembly's metadata, so a consumer compiled against the published package would have thrown
        // MissingMethodException without recompiling; the one-argument form is therefore a real overload.
        // It forwards isDelimited: true, matching its sibling
        // EvaluationContext.TryGetEnvironmentVariable(string, out object?) and preserving what a
        // one-argument call returned before the flag existed - the prefix alone was enough. The spelling
        // still decides for a caller that knows it, which the two-argument assertion below pins.
        //
        // The metadata claim is asserted against metadata, not against a call. An earlier revision of this
        // comment said the calls below were what fails if someone collapses the two into one optional
        // parameter; they are not, and that was reproduced - deleting ResolveVariable(string) and rewriting
        // the other as ResolveVariable(string name, bool isDelimited = true) left this test passing 2/2.
        // Overload-versus-default is invisible at source level, which is the whole reason the distinction
        // matters to an already-compiled consumer.
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
