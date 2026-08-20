// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using Ignixa.FhirPath.Parser;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.Definition;

public class ReferenceIdentifierSearchParameterFactoryTests
{
    private readonly SearchParameterInfo _referenceParameter = new(
        name: "subject",
        code: "subject",
        searchParamType: SearchParamType.Reference,
        url: new Uri("http://hl7.org/fhir/SearchParameter/Encounter-subject"),
        expression: "Encounter.subject",
        targetResourceTypes: ["Group", "Patient"],
        baseResourceTypes: ["Encounter"]);

    [Fact]
    public void GivenReferenceParameter_WhenDerivingIdentity_ThenUrlAndCodeMatchContract()
    {
        Uri url = ReferenceIdentifierSearchParameterFactory.DeriveUrl(_referenceParameter);
        string code = ReferenceIdentifierSearchParameterFactory.DeriveCode(_referenceParameter.Code);

        url.OriginalString.ShouldBe("http://hl7.org/fhir/SearchParameter/Encounter-subject#identifier");
        code.ShouldBe("subject:identifier");
    }

    [Fact]
    public void GivenReferenceParameter_WhenCreatingDerivedParameter_ThenContractIsPreserved()
    {
        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(_referenceParameter);

        derived.Name.ShouldBe("subject:identifier");
        derived.Code.ShouldBe("subject:identifier");
        derived.Url.OriginalString.ShouldBe("http://hl7.org/fhir/SearchParameter/Encounter-subject#identifier");
        derived.Type.ShouldBe(SearchParamType.Token);
        derived.Expression.ShouldBe("(Encounter.subject).select(ofType(Reference) | ofType(CodeableReference).reference).identifier");
        derived.BaseResourceTypes.ShouldBe(_referenceParameter.BaseResourceTypes);
        derived.TargetResourceTypes.ShouldBeEmpty();
        derived.IsSupported.ShouldBeTrue();
        derived.IsSearchable.ShouldBeTrue();
        derived.IsDerived.ShouldBeTrue();
        derived.ShouldNotBe(_referenceParameter);
    }

    [Fact]
    public void GivenReferenceParameterWithoutExpression_WhenCreatingDerivedParameter_ThenItHasNoExpressionEither()
    {
        var unindexed = new SearchParameterInfo(
            name: "subject",
            code: "subject",
            searchParamType: SearchParamType.Reference,
            url: new Uri("http://example.org/SearchParameter/unindexed"),
            expression: null,
            targetResourceTypes: ["Patient"],
            baseResourceTypes: ["Encounter"]);

        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(unindexed);

        derived.Expression.ShouldBeEmpty();
    }

    [Fact]
    public void GivenOriginalAndDerivedUrls_WhenComparingIdentity_ThenFragmentsRemainDistinct()
    {
        Uri derivedUrl = ReferenceIdentifierSearchParameterFactory.DeriveUrl(_referenceParameter);

        SearchParameterUriComparer.Instance.Equals(_referenceParameter.Url, derivedUrl).ShouldBeFalse();
        SearchParameterUriComparer.Instance.GetHashCode(_referenceParameter.Url)
            .ShouldNotBe(SearchParameterUriComparer.Instance.GetHashCode(derivedUrl));
    }

    [Fact]
    public void GivenSingleTargetTypeFilteredExpression_WhenCreatingDerivedParameter_ThenResolveTestAlsoAcceptsReferenceType()
    {
        var typeFiltered = ReferenceParameterWithExpression("Encounter.subject.where(resolve() is Patient)");

        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(typeFiltered);

        derived.Expression.ShouldBe(
            "(Encounter.subject.where((resolve() is Patient or type = 'Patient' or " +
            "type = 'http://hl7.org/fhir/StructureDefinition/Patient')))" +
            ".select(ofType(Reference) | ofType(CodeableReference).reference).identifier");
    }

    [Fact]
    public void GivenOrChainedMultiTargetTypeFilteredExpression_WhenCreatingDerivedParameter_ThenEachResolveTestIsIndependentlyRewritten()
    {
        var multiTarget = ReferenceParameterWithExpression(
            "Appointment.participant.actor.where(resolve() is Group) | Appointment.subject.where(resolve() is Group)");

        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(multiTarget);

        derived.Expression.ShouldBe(
            "(Appointment.participant.actor.where((resolve() is Group or type = 'Group' or " +
            "type = 'http://hl7.org/fhir/StructureDefinition/Group')) | Appointment.subject.where((resolve() is Group or " +
            "type = 'Group' or type = 'http://hl7.org/fhir/StructureDefinition/Group')))" +
            ".select(ofType(Reference) | ofType(CodeableReference).reference).identifier");
    }

    [Fact]
    public void GivenUnionedCompartmentExpressionWithManyTypeFilteredBranches_WhenCreatingDerivedParameter_ThenEveryBranchResolveTestIsRewritten()
    {
        var compartment = ReferenceParameterWithExpression(
            "AllergyIntolerance.patient | CarePlan.subject.where(resolve() is Patient) | Consent.patient | " +
            "Encounter.subject.where(resolve() is Patient)");

        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(compartment);

        derived.Expression.ShouldBe(
            "(AllergyIntolerance.patient | CarePlan.subject.where((resolve() is Patient or type = 'Patient' or " +
            "type = 'http://hl7.org/fhir/StructureDefinition/Patient')) | Consent.patient | " +
            "Encounter.subject.where((resolve() is Patient or type = 'Patient' or " +
            "type = 'http://hl7.org/fhir/StructureDefinition/Patient')))" +
            ".select(ofType(Reference) | ofType(CodeableReference).reference).identifier");
    }

    [Fact]
    public void GivenExpressionWithNoResolveTypeTest_WhenCreatingDerivedParameter_ThenExpressionIsUnchangedApartFromWrapping()
    {
        // Encounter-subject itself (unlike clinical-patient's Encounter.subject branch) is not type-filtered:
        // it targets both Group and Patient unfiltered, so there is no 'resolve() is X' test to rewrite.
        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(_referenceParameter);

        derived.Expression.ShouldBe(
            "(Encounter.subject).select(ofType(Reference) | ofType(CodeableReference).reference).identifier");
    }

    [Fact]
    public void GivenNamespacedTypeTestShape_WhenCreatingDerivedParameter_ThenItIsLeftUnrewritten()
    {
        // 'FHIR.Patient' does not occur in any generated base parameter; the trailing negative lookahead
        // refuses to match a captured type name immediately followed by '.', so this hypothetical shape is
        // deliberately left untouched rather than rewritten against only its 'FHIR' segment.
        var namespacedShape = ReferenceParameterWithExpression("Encounter.subject.where(resolve() is FHIR.Patient)");

        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(namespacedShape);

        derived.Expression.ShouldBe(
            "(Encounter.subject.where(resolve() is FHIR.Patient))" +
            ".select(ofType(Reference) | ofType(CodeableReference).reference).identifier");
    }

    [Fact]
    public void GivenStringLiteralContainingTheResolveIsPhrase_WhenCreatingDerivedParameter_ThenTheLiteralIsLeftUnrewrittenAndTheExpressionParses()
    {
        // Reviewer repro: 'resolve() is Patient' here is literal text being compared against Patient.name,
        // not FHIRPath syntax. A literal-unaware rewrite would corrupt the string and the derived expression
        // would fail to parse (e.g. "(... or type = '(resolve() is Patient or type = 'Patient' or ...')...)").
        var literalShape = ReferenceParameterWithExpression("Patient.name.where(text = 'resolve() is Patient')");

        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(literalShape);

        derived.Expression.ShouldBe(
            "(Patient.name.where(text = 'resolve() is Patient'))" +
            ".select(ofType(Reference) | ofType(CodeableReference).reference).identifier");
        Should.NotThrow(() => new FhirPathParser().Parse(derived.Expression));
    }

    [Fact]
    public void GivenEscapedQuoteInsideALiteralFollowedByARealResolveIsTestOutsideIt_WhenCreatingDerivedParameter_ThenOnlyTheOutsideOccurrenceIsRewritten()
    {
        // The literal contains a backslash-escaped quote (\') immediately before the phrase
        // 'resolve() is Patient'; the escaped quote must not be mistaken for the literal's closing
        // delimiter, which would spill the rest of the literal out as if it were real FHIRPath syntax.
        // The real 'resolve() is Patient' test that follows, outside the literal, is still rewritten.
        var mixed = ReferenceParameterWithExpression(
            @"Patient.name.where(text = 'it\'s not a resolve() is Patient test').subject.where(resolve() is Patient)");

        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(mixed);

        derived.Expression.ShouldBe(
            @"(Patient.name.where(text = 'it\'s not a resolve() is Patient test').subject.where((resolve() is Patient or type = 'Patient' or " +
            "type = 'http://hl7.org/fhir/StructureDefinition/Patient')))" +
            ".select(ofType(Reference) | ofType(CodeableReference).reference).identifier");
        Should.NotThrow(() => new FhirPathParser().Parse(derived.Expression));
    }

    [Fact]
    public void GivenMixedExpressionWithBothLiteralAndRealResolveIsOccurrences_WhenCreatingDerivedParameter_ThenOnlyTheRealOccurrenceIsRewritten()
    {
        // One union branch has a real type test to rewrite; the other compares $this against a string
        // literal that happens to contain the same phrase as plain text. Only the real occurrence changes.
        var mixed = ReferenceParameterWithExpression(
            "Encounter.subject.where(resolve() is Patient) | Encounter.note.text.where($this = 'resolve() is Group')");

        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(mixed);

        derived.Expression.ShouldBe(
            "(Encounter.subject.where((resolve() is Patient or type = 'Patient' or " +
            "type = 'http://hl7.org/fhir/StructureDefinition/Patient')) | " +
            "Encounter.note.text.where($this = 'resolve() is Group'))" +
            ".select(ofType(Reference) | ofType(CodeableReference).reference).identifier");
        Should.NotThrow(() => new FhirPathParser().Parse(derived.Expression));
    }

    [Fact]
    public void GivenHugeUnclosedQuoteInsideALineComment_WhenCreatingDerivedParameter_ThenTheRewriteCompletesQuicklyAndParses()
    {
        // Reviewer repro (I1): an unclosed ' is legal inside a FHIRPath '//' comment, e.g.
        // "Observation.subject // '\'\'\'...". The prior pattern had no comment alternative, so each of
        // the ~32K backslash-escaped quotes in this comment re-triggered an unbounded string-literal
        // match attempt that scanned to end-of-input before failing - quadratic in the comment's length
        // (32 KB measured at ~16s through Create). A bare run of plain quote characters does not trigger
        // this (the greedy '' pairing backtracks out in one step), so the adversarial input must use the
        // same backslash-escape form as the reviewer's repro to actually exercise the prior bug. The real
        // 'resolve() is Patient' test before the comment must still be rewritten, and the result must
        // still parse, proving the comment itself is passed through untouched rather than stripped.
        var hugeEscapedQuoteRun = new StringBuilder("// '");
        for (int i = 0; i < 32 * 1024; i++)
        {
            hugeEscapedQuoteRun.Append(@"\'");
        }

        // The comment ends with a newline (rather than running to the very end of the string) purely so
        // the derived expression's appended ".select(...)" suffix remains outside the comment and the
        // overall expression stays parseable.
        hugeEscapedQuoteRun.Append('\n');
        var pathological = ReferenceParameterWithExpression(
            $"Observation.subject.where(resolve() is Patient) {hugeEscapedQuoteRun}");

        var stopwatch = Stopwatch.StartNew();
        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(pathological);
        stopwatch.Stop();

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
        derived.Expression.ShouldStartWith(
            "(Observation.subject.where((resolve() is Patient or type = 'Patient' or " +
            "type = 'http://hl7.org/fhir/StructureDefinition/Patient')) // '");
        derived.Expression.ShouldEndWith("\n).select(ofType(Reference) | ofType(CodeableReference).reference).identifier");
        Should.NotThrow(() => new FhirPathParser().Parse(derived.Expression));
    }

    [Fact]
    public void GivenBlockCommentContainingAnApostropheBeforeARealResolveIsTest_WhenCreatingDerivedParameter_ThenTheRealOneIsRewritten()
    {
        // The block comment contains an apostrophe (in "don't") that the prior, comment-unaware pattern
        // would have mistaken for the start of an unterminated string literal, corrupting everything
        // after it. The real 'resolve() is Patient' test that follows the comment is still rewritten.
        var mixed = ReferenceParameterWithExpression("/* don't */ Encounter.subject.where(resolve() is Patient)");

        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(mixed);

        derived.Expression.ShouldBe(
            "(/* don't */ Encounter.subject.where((resolve() is Patient or type = 'Patient' or " +
            "type = 'http://hl7.org/fhir/StructureDefinition/Patient')))" +
            ".select(ofType(Reference) | ofType(CodeableReference).reference).identifier");
        Should.NotThrow(() => new FhirPathParser().Parse(derived.Expression));
    }

    [Fact]
    public void GivenResolveIsTestInsideALineComment_WhenCreatingDerivedParameter_ThenItIsLeftUnrewritten()
    {
        // The comment ends with a newline so the derived expression's appended suffix stays outside it
        // and the overall expression remains parseable (a '//' comment otherwise runs to end-of-input).
        var commented = ReferenceParameterWithExpression("Encounter.subject // resolve() is Patient\n");

        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(commented);

        derived.Expression.ShouldBe(
            "(Encounter.subject // resolve() is Patient\n)" +
            ".select(ofType(Reference) | ofType(CodeableReference).reference).identifier");
        Should.NotThrow(() => new FhirPathParser().Parse(derived.Expression));
    }

    [Fact]
    public void GivenResolveIsTestInsideABlockComment_WhenCreatingDerivedParameter_ThenItIsLeftUnrewritten()
    {
        var commented = ReferenceParameterWithExpression("Encounter.subject /* resolve() is Patient */");

        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(commented);

        derived.Expression.ShouldBe(
            "(Encounter.subject /* resolve() is Patient */)" +
            ".select(ofType(Reference) | ofType(CodeableReference).reference).identifier");
        Should.NotThrow(() => new FhirPathParser().Parse(derived.Expression));
    }

    [Fact]
    public void GivenDoubleQuotedIdentifierContainingTheResolveIsPhrase_WhenCreatingDerivedParameter_ThenItIsLeftUnrewritten()
    {
        // A legacy double-quoted delimited identifier can legally contain the same text this rewrite
        // targets; it must be passed through as an opaque identifier, not treated as syntax.
        var legacyIdentifier = ReferenceParameterWithExpression("Encounter.\"resolve() is Patient\"");

        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(legacyIdentifier);

        derived.Expression.ShouldBe(
            "(Encounter.\"resolve() is Patient\")" +
            ".select(ofType(Reference) | ofType(CodeableReference).reference).identifier");
        Should.NotThrow(() => new FhirPathParser().Parse(derived.Expression));
    }

    [Fact]
    public void GivenDottedResolveReceiver_WhenCreatingDerivedParameter_ThenItIsLeftUnrewrittenAndParses()
    {
        // Reviewer repro (I2): a dotted receiver such as 'subject.resolve() is Patient' must not be
        // rewritten. Splicing a parenthesized expression directly after '.' does not parse
        // ("subject.(resolve() is Patient or ...)"), and even a corrected rewrite would need 'type' to
        // be evaluated against 'subject', not whatever focus 'where(...)' itself operates on - which
        // this regex cannot determine. The negative lookbehind before 'resolve' leaves this untouched.
        var dotted = ReferenceParameterWithExpression("Observation.where(subject.resolve() is Patient).subject");

        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(dotted);

        derived.Expression.ShouldBe(
            "(Observation.where(subject.resolve() is Patient).subject)" +
            ".select(ofType(Reference) | ofType(CodeableReference).reference).identifier");
        Should.NotThrow(() => new FhirPathParser().Parse(derived.Expression));
    }

    [Fact]
    public void GivenReferenceParameter_WhenCreatingDerivedParameter_ThenSortingIsDisabled()
    {
        // The base constructor defaults SortStatus to Enabled for every Token-typed parameter, but
        // sorting by an Identifier derived purely to make :identifier searchable has no defined
        // meaning, and SearchOptionsBuilder.ParseSortParameters looks sort fields up by raw code, so
        // an unqualified "_sort=subject:identifier" must not silently be accepted (M5).
        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(_referenceParameter);

        derived.SortStatus.ShouldBe(SortParameterStatus.Disabled);
    }

    private static SearchParameterInfo ReferenceParameterWithExpression(string expression) => new(
        name: "subject",
        code: "subject",
        searchParamType: SearchParamType.Reference,
        url: new Uri("http://hl7.org/fhir/SearchParameter/Encounter-subject"),
        expression: expression,
        targetResourceTypes: ["Group", "Patient"],
        baseResourceTypes: ["Encounter"]);
}
