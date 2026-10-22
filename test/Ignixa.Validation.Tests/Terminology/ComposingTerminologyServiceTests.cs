// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;
using Ignixa.Validation.Services.Terminology;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Terminology;

public class ComposingTerminologyServiceTests
{
    private const string System = "http://example.org/cs";
    private const string ValueSet = "http://example.org/vs";

    private readonly ITerminologySource _sql = Source("sql");
    private readonly ITerminologySource _package = Source("package");

    private static ITerminologySource Source(string name)
    {
        var source = Substitute.For<ITerminologySource, ILegacyValueSetOperations>();
        source.Name.Returns(name);
#pragma warning disable CA2012
        source.GetCoverageAsync(Arg.Any<CanonicalKind>(), Arg.Any<CanonicalRef>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<SourceCoverage>(SourceCoverage.None));
#pragma warning restore CA2012
        return source;
    }

    private static void Covers(ITerminologySource source, CanonicalKind kind, SourceCoverage coverage)
    {
#pragma warning disable CA2012
        source.GetCoverageAsync(kind, Arg.Any<CanonicalRef>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<SourceCoverage>(coverage));
#pragma warning restore CA2012
    }

    private static ILegacyValueSetOperations Legacy(ITerminologySource source) => (ILegacyValueSetOperations)source;

    private ComposingTerminologyService Create() => new([_sql, _package]);

    [Fact]
    public async Task GivenImportedCodeSystem_WhenLookingUp_ThenSqlAnswersAndPackageIsNotAsked()
    {
        // A router that queries a partial source after authoritative SQL would be a correctness bug.
        Covers(_sql, CanonicalKind.CodeSystem, SourceCoverage.Authoritative);
        Covers(_package, CanonicalKind.CodeSystem, SourceCoverage.Partial);
        _sql.LookupAsync(Arg.Any<CodeRef>(), Arg.Any<CancellationToken>()).Returns(ConceptLookup.NotFound);

        var lookup = await Create().LookupAsync(new LookupRequest(new CodeRef(System, "x")), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.False);
        await _package.DidNotReceive().LookupAsync(Arg.Any<CodeRef>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenUnimportedCodeSystem_WhenLookingUp_ThenPackageAnswers()
    {
        // A router that skips a partial package source would make package-known codes unavailable.
        Covers(_package, CanonicalKind.CodeSystem, SourceCoverage.Partial);
        var details = new ConceptDetails(null, null, "Car", null, null, null);
        _package.LookupAsync(Arg.Any<CodeRef>(), Arg.Any<CancellationToken>()).Returns(ConceptLookup.Found(details));

        var lookup = await Create().LookupAsync(new LookupRequest(new CodeRef(System, "car")), CancellationToken.None);

        lookup.Concept.ShouldBe(details);
        await _sql.DidNotReceive().LookupAsync(Arg.Any<CodeRef>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenNoSourceHoldsTheSystem_WhenSubsumesAsked_ThenUndeterminedNeverNotSubsumed()
    {
        // Regression: Hybrid sent every $subsumes to SQL, which answered not-subsumed for an unknown system.
        var request = new SubsumesRequest(new CodeRef(System, "a"), new CodeRef(System, "b"));

        var outcome = await Create().SubsumesAsync(request, CancellationToken.None);

        outcome.Unknown!.Kind.ShouldBe(UnknownKind.SystemNotAvailable);
        outcome.KnownRelation.ShouldBeNull();
    }

    [Fact]
    public async Task GivenUnknownCode_WhenValidatingInCodeSystem_ThenResultIsUnknownWithReasons()
    {
        // Treating an unknown source answer as false would report a code invalid without evidence.
        var reason = new UnknownReason(UnknownKind.NonEnumerableSystem, System, "subset");
        Covers(_package, CanonicalKind.CodeSystem, SourceCoverage.Partial);
        _package.LookupAsync(Arg.Any<CodeRef>(), Arg.Any<CancellationToken>()).Returns(ConceptLookup.Undetermined(reason));

        var result = await Create().ValidateCodeInCodeSystemAsync(
            new ValidateCodeRequest(new CodeRef(System, "x")),
            CancellationToken.None);

        result.Result.ShouldBe(Determination.Unknown);
        result.Unknowns.ShouldBe([reason]);
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAbsentCode_WhenValidatingInCodeSystem_ThenFalseWithCodeInvalidError()
    {
        // An authoritative miss is a fact and must become a CodeInvalid error.
        Covers(_sql, CanonicalKind.CodeSystem, SourceCoverage.Authoritative);
        _sql.LookupAsync(Arg.Any<CodeRef>(), Arg.Any<CancellationToken>()).Returns(ConceptLookup.NotFound);

        var result = await Create().ValidateCodeInCodeSystemAsync(
            new ValidateCodeRequest(new CodeRef(System, "x")),
            CancellationToken.None);

        result.Result.ShouldBe(Determination.False);
        result.Issues.ShouldHaveSingleItem().Kind.ShouldBe(TerminologyIssueKind.CodeInvalid);
    }

    [Fact]
    public async Task GivenWrongDisplay_WhenValidatingInCodeSystem_ThenTrueWithDisplayWarning()
    {
        // Rejecting a valid code because only its display differs would violate CodeSystem validation semantics.
        Covers(_sql, CanonicalKind.CodeSystem, SourceCoverage.Authoritative);
        _sql.LookupAsync(Arg.Any<CodeRef>(), Arg.Any<CancellationToken>())
            .Returns(ConceptLookup.Found(new ConceptDetails(null, null, "Car", null, null, null)));

        var result = await Create().ValidateCodeInCodeSystemAsync(
            new ValidateCodeRequest(new CodeRef(System, "car"), "Automobile"),
            CancellationToken.None);

        result.Result.ShouldBe(Determination.True);
        result.Display.ShouldBe("Car");
        var issue = result.Issues.ShouldHaveSingleItem();
        issue.Kind.ShouldBe(TerminologyIssueKind.DisplayMismatch);
        issue.Severity.ShouldBe(IssueSeverity.Warning);
    }

    [Fact]
    public async Task GivenImportedValueSet_WhenValidatingABinding_ThenSqlLegacyIsUsed()
    {
        // Legacy binding validation must retain its imported-ValueSet routing while Phase 2 is pending.
        Covers(_sql, CanonicalKind.ValueSet, SourceCoverage.Authoritative);

        await Create().ValidateBindingAsync(ValueSet, BindingStrength.Required, System, "x", null, null, CancellationToken.None);

        await Legacy(_sql).Received(1).ValidateBindingAsync(
            ValueSet, BindingStrength.Required, System, "x", null, null, Arg.Any<CancellationToken>());
        await Legacy(_package).DidNotReceiveWithAnyArgs().ValidateBindingAsync(
            default!, default, default, default, default, default, default);
    }

    [Fact]
    public async Task GivenUnimportedValueSet_WhenValidatingABinding_ThenPackageLegacyIsTheFallback()
    {
        // Falling back to SQL here changes the exact legacy ValidateBindingAsync output.
        await Create().ValidateBindingAsync(ValueSet, BindingStrength.Required, System, "x", null, null, CancellationToken.None);

        await Legacy(_package).Received(1).ValidateBindingAsync(
            ValueSet, BindingStrength.Required, System, "x", null, null, Arg.Any<CancellationToken>());
        await Legacy(_sql).DidNotReceiveWithAnyArgs().ValidateBindingAsync(
            default!, default, default, default, default, default, default);
    }

    [Fact]
    public async Task GivenUnimportedValueSet_WhenValidatingACode_ThenPackageLegacyIsTheFallback()
    {
        // ValueSet validate-code stays on legacy routing until Phase 2.
        await Create().ValidateCodeAsync(System, "x", null, ValueSet, CancellationToken.None);

        await Legacy(_package).Received(1).ValidateCodeAsync(
            System, "x", null, ValueSet, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenImportedValueSet_WhenExpanding_ThenSqlLegacyIsUsed()
    {
        // ValueSet expansion stays on legacy routing until Phase 4.
        Covers(_sql, CanonicalKind.ValueSet, SourceCoverage.Authoritative);
        var parameters = new ExpansionParameters(ValueSet);

        await Create().ExpandValueSetAsync(parameters, CancellationToken.None);

        await Legacy(_sql).Received(1).ExpandValueSetAsync(parameters, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GivenNoValueSetUrl_WhenValidating_ThenErrorWithoutConsultingSources(string? url)
    {
        // Consulting a source without a ValueSet URL would replace Hybrid's caller-visible validation error.
        var code = await Create().ValidateCodeAsync(System, "x", null, url, CancellationToken.None);
        var binding = await Create().ValidateBindingAsync(url!, BindingStrength.Required, System, "x", null, null, CancellationToken.None);

        code.ShouldBe(new TerminologyValidationResult(false, IssueSeverity.Error, "ValueSet URL is required"));
        binding.ShouldBe(new BindingValidationResult(
            false, BindingStrength.Required, IssueSeverity.Error, "ValueSet URL is required", null));
        await _sql.DidNotReceiveWithAnyArgs().GetCoverageAsync(default, default!, default);
    }

    [Fact]
    public async Task GivenAnyImportState_WhenTranslating_ThenFirstLegacySourceIsUsedWithoutCoverageQuery()
    {
        // No ConceptMap coverage exists in Phase 1, so translation retains its first-source legacy path.
        var parameters = new TranslateParameters(null, null, "x", System, null, null, null, null);

        await Create().TranslateCodeAsync(parameters, CancellationToken.None);

        await Legacy(_sql).Received(1).TranslateCodeAsync(parameters, Arg.Any<CancellationToken>());
        await _sql.DidNotReceiveWithAnyArgs().GetCoverageAsync(default, default!, default);
    }

    [Fact]
    public void GivenNoSources_WhenConstructed_ThenThrows()
    {
        // Constructing a composer that can answer no operation is an invalid configuration.
        Should.Throw<ArgumentException>(() => new ComposingTerminologyService([]));
    }
}
