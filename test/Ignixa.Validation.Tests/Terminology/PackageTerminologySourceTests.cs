// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;
using Ignixa.Validation.Services.Terminology;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Terminology;

public class PackageTerminologySourceTests
{
    private const string Local = "http://example.org/cs";
    private const string ValueSet = "http://example.org/vs";

    private static PackageTerminologySource Create(ICodeSystemProvider? codeSystems = null, IValueSetProvider? valueSets = null) =>
        new(valueSets ?? Substitute.For<IValueSetProvider>(), codeSystems);

    [Fact]
    public async Task GivenEnumeratedCode_WhenLookedUp_ThenFoundWithProviderDisplay()
    {
        var codeSystems = Substitute.For<ICodeSystemProvider>();
        codeSystems.ContainsCode(Local, "car").Returns(true);
        codeSystems.GetDisplay(Local, "car").Returns("Car");

        var lookup = await Create(codeSystems).LookupAsync(new CodeRef(Local, "car"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.True);
        lookup.Concept!.Display.ShouldBe("Car");
    }

    [Fact]
    public async Task GivenEnumerableSystemWithoutTheCode_WhenLookedUp_ThenNotFound()
    {
        var codeSystems = Substitute.For<ICodeSystemProvider>();
        codeSystems.ContainsCode(Local, "boat").Returns(false);

        var lookup = await Create(codeSystems).LookupAsync(new CodeRef(Local, "boat"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.False);
    }

    [Theory]
    [InlineData("http://snomed.info/sct")]
    [InlineData("http://loinc.org")]
    [InlineData("http://unitsofmeasure.org")]
    [InlineData("http://www.nlm.nih.gov/research/umls/rxnorm")]
    [InlineData("http://hl7.org/fhir/sid/icd-10")]
    [InlineData("http://hl7.org/fhir/sid/icd-10-cm")]
    [InlineData("http://hl7.org/fhir/sid/icd-9-cm")]
    [InlineData("http://hl7.org/fhir/sid/cvx")]
    [InlineData("http://www.ama-assn.org/go/cpt")]
    public async Task GivenNonEnumerableSystemMiss_WhenLookedUp_ThenUnknownNonEnumerable(string system)
    {
        var codeSystems = Substitute.For<ICodeSystemProvider>();
        codeSystems.ContainsCode(system, "123").Returns(false);

        var lookup = await Create(codeSystems).LookupAsync(new CodeRef(system, "123"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.Unknown);
        lookup.Unknown!.Kind.ShouldBe(UnknownKind.NonEnumerableSystem);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenSystemUnknownToProvider_WhenLookedUp_ThenUnknownSystemNotAvailable(bool hasProvider)
    {
        var codeSystems = hasProvider ? Substitute.For<ICodeSystemProvider>() : null;
        codeSystems?.ContainsCode(Local, "car").Returns((bool?)null);

        var lookup = await Create(codeSystems).LookupAsync(new CodeRef(Local, "car"), CancellationToken.None);

        lookup.Unknown!.Kind.ShouldBe(UnknownKind.SystemNotAvailable);
    }

    [Fact]
    public async Task GivenAnyCodes_WhenSubsumesAsked_ThenUndeterminedWithUnsupportedFilter()
    {
        var outcome = await Create().SubsumesAsync(new CodeRef(Local, "a"), new CodeRef(Local, "b"), CancellationToken.None);

        outcome.ShouldBeOfType<SubsumesOutcome.Undetermined>();
        outcome.Unknown!.Kind.ShouldBe(UnknownKind.UnsupportedFilter);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenExplicitVersion_WhenLookedUp_ThenUnknownSystemVersionNotAvailable(bool packageHasCode)
    {
        // Regression: package content is unversioned, so an explicit version answered Found/NotFound from
        // whatever version the package happened to carry instead of admitting it cannot prove that version.
        var codeSystems = Substitute.For<ICodeSystemProvider>();
        codeSystems.ContainsCode(Local, "car").Returns(packageHasCode);
        codeSystems.GetDisplay(Local, "car").Returns("Car");

        var lookup = await Create(codeSystems).LookupAsync(new CodeRef(Local, "car", "2.0"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.Unknown);
        lookup.Concept.ShouldBeNull();
        lookup.Unknown!.Kind.ShouldBe(UnknownKind.SystemVersionNotAvailable);
        lookup.Unknown.Subject.ShouldBe($"{Local}|2.0");
    }

    [Fact]
    public async Task GivenExplicitVersion_WhenSubsumesAsked_ThenUnknownSystemVersionNotAvailable()
    {
        // Regression: the version was ignored, hiding that the package cannot prove the requested version.
        var outcome = await Create().SubsumesAsync(
            new CodeRef(Local, "a", "2.0"), new CodeRef(Local, "b", "2.0"), CancellationToken.None);

        outcome.KnownRelation.ShouldBeNull();
        outcome.Unknown!.Kind.ShouldBe(UnknownKind.SystemVersionNotAvailable);
        outcome.Unknown.Subject.ShouldBe($"{Local}|2.0");
    }

    [Fact]
    public void GivenNullValueSetProvider_WhenConstructed_ThenThrowsForThatArgument()
    {
        Should.Throw<ArgumentNullException>(() => new PackageTerminologySource(null!))
            .ParamName.ShouldBe("valueSetProvider");
    }

    [Fact]
    public void GivenNullPrimaryProvider_WhenLayeredSourceConstructed_ThenThrowsForThatArgument()
    {
        // Regression: a null primary was accepted and failed later, on the first ValueSet fallback.
        Should.Throw<ArgumentNullException>(
                () => new PackageTerminologySource(null!, [Substitute.For<IValueSetProvider>()]))
            .ParamName.ShouldBe("primary");
    }

    [Fact]
    public void GivenNullAdditionalProviders_WhenLayeredSourceConstructed_ThenThrowsForThatArgument()
    {
        // Regression: a null list surfaced as an exception about LINQ's 'source' parameter.
        Should.Throw<ArgumentNullException>(
                () => new PackageTerminologySource(Substitute.For<IValueSetProvider>(), (IEnumerable<IValueSetProvider>)null!))
            .ParamName.ShouldBe("additional");
    }

    [Fact]
    public async Task GivenNullCode_WhenLookedUp_ThenThrowsForThatArgument()
    {
        (await Should.ThrowAsync<ArgumentNullException>(() => Create().LookupAsync(null!, CancellationToken.None)))
            .ParamName.ShouldBe("code");
    }

    [Theory]
    [InlineData(CanonicalKind.CodeSystem, false, SourceCoverage.Partial)]
    [InlineData(CanonicalKind.ValueSet, true, SourceCoverage.Authoritative)]
    [InlineData(CanonicalKind.ValueSet, false, SourceCoverage.None)]
    [InlineData(CanonicalKind.ConceptMap, true, SourceCoverage.None)]
    public async Task GivenCanonicalKind_WhenCoverageAsked_ThenSpecRuleApplies(CanonicalKind kind, bool knownValueSet, SourceCoverage expected)
    {
        var valueSets = Substitute.For<IValueSetProvider>();
        valueSets.IsKnownValueSet("http://example.org/x").Returns(knownValueSet);

        var coverage = await Create(valueSets: valueSets).GetCoverageAsync(kind, new CanonicalRef("http://example.org/x"), CancellationToken.None);

        coverage.ShouldBe(expected);
    }

    [Fact]
    public async Task GivenVersionedValueSetUrl_WhenValidatingAnIncludedCode_ThenUsesTheUnversionedExpansion()
    {
        var valueSets = Substitute.For<IValueSetProvider>();
        valueSets.GetCodes(ValueSet).Returns([new FhirCode(Local, "car", "Car")]);

        var result = await Create(valueSets: valueSets).ValidateCodeAsync(
            Local, "car", null, $"{ValueSet}|1.0", CancellationToken.None);

        result.IsValid.ShouldBeTrue();
        result.Severity.ShouldBe(IssueSeverity.Information);
    }

    [Theory]
    [InlineData("http://snomed.info/sct", true, IssueSeverity.Warning)]
    [InlineData("http://loinc.org", true, IssueSeverity.Warning)]
    [InlineData("http://unitsofmeasure.org", true, IssueSeverity.Warning)]
    [InlineData("http://www.nlm.nih.gov/research/umls/rxnorm", true, IssueSeverity.Warning)]
    [InlineData("http://hl7.org/fhir/sid/icd-10", true, IssueSeverity.Warning)]
    [InlineData("http://hl7.org/fhir/sid/icd-10-cm", true, IssueSeverity.Warning)]
    [InlineData("http://hl7.org/fhir/sid/icd-9-cm", true, IssueSeverity.Warning)]
    [InlineData("http://hl7.org/fhir/sid/cvx", true, IssueSeverity.Warning)]
    [InlineData("http://www.ama-assn.org/go/cpt", true, IssueSeverity.Warning)]
    [InlineData(Local, false, IssueSeverity.Error)]
    public async Task GivenMissingCodeInSystem_WhenValidatingValueSet_ThenOnlyEnumerableSystemsAreInvalid(
        string system,
        bool expectedValidity,
        IssueSeverity expectedSeverity)
    {
        var valueSets = Substitute.For<IValueSetProvider>();
        valueSets.GetCodes(ValueSet).Returns([new FhirCode(Local, "car", "Car")]);

        var result = await Create(valueSets: valueSets).ValidateCodeAsync(
            system, "missing", null, ValueSet, CancellationToken.None);

        result.IsValid.ShouldBe(expectedValidity);
        result.Severity.ShouldBe(expectedSeverity);
    }

    [Fact]
    public async Task GivenMissingCodeInEnumeratedNonEnumerableSystem_WhenValidatingValueSet_ThenResultIsUnverifiable()
    {
        var valueSets = Substitute.For<IValueSetProvider>();
        valueSets.GetCodes(ValueSet).Returns([new FhirCode("http://snomed.info/sct", "included", "Included")]);

        var result = await Create(valueSets: valueSets).ValidateCodeAsync(
            "http://snomed.info/sct", "missing", null, ValueSet, CancellationToken.None);

        result.IsValid.ShouldBeTrue();
        result.Severity.ShouldBe(IssueSeverity.Warning);
    }

    [Theory]
    [InlineData(BindingStrength.Required, "car", true, IssueSeverity.Information)]
    [InlineData(BindingStrength.Required, "missing", false, IssueSeverity.Error)]
    [InlineData(BindingStrength.Extensible, "car", true, IssueSeverity.Information)]
    [InlineData(BindingStrength.Extensible, "missing", true, IssueSeverity.Warning)]
    [InlineData(BindingStrength.Preferred, "car", true, IssueSeverity.Information)]
    [InlineData(BindingStrength.Preferred, "missing", true, IssueSeverity.Error)]
    [InlineData(BindingStrength.Example, "car", true, IssueSeverity.Information)]
    [InlineData(BindingStrength.Example, "missing", true, IssueSeverity.Error)]
    public async Task GivenBindingStrengthAndCodeMembership_WhenValidatingBinding_ThenMapsValidityAndSeverity(
        BindingStrength strength,
        string code,
        bool expectedValidity,
        IssueSeverity expectedSeverity)
    {
        var valueSets = Substitute.For<IValueSetProvider>();
        valueSets.GetCodes(ValueSet).Returns([new FhirCode(Local, "car", "Car")]);

        var result = await Create(valueSets: valueSets).ValidateBindingAsync(
            ValueSet, strength, Local, code, null, null, CancellationToken.None);

        result.IsValid.ShouldBe(expectedValidity);
        result.Severity.ShouldBe(expectedSeverity);
    }

    [Fact]
    public async Task GivenPrimaryDoesNotKnowValueSet_WhenAdditionalKnowsIt_ThenCoverageUsesAdditionalProvider()
    {
        var primary = Substitute.For<IValueSetProvider>();
        primary.IsKnownValueSet(ValueSet).Returns(false);
        var additional = Substitute.For<IValueSetProvider>();
        additional.IsKnownValueSet(ValueSet).Returns(true);
        var source = new PackageTerminologySource(primary, [additional]);

        var coverage = await source.GetCoverageAsync(
            CanonicalKind.ValueSet, new CanonicalRef(ValueSet), CancellationToken.None);

        coverage.ShouldBe(SourceCoverage.Authoritative);
    }

    [Fact]
    public async Task GivenAnyTranslateRequest_WhenTranslating_ThenNoMatchesAreReturned()
    {
        var result = await Create().TranslateCodeAsync(
            new TranslateParameters(null, null, "car", Local, null, null, null, null),
            CancellationToken.None);

        result.Result.ShouldBeFalse();
        result.Matches.ShouldBeEmpty();
    }
}
