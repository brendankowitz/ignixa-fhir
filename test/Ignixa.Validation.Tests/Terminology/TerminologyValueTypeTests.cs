// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions.Terminology;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Terminology;

public class TerminologyValueTypeTests
{
    [Theory]
    [InlineData("http://loinc.org", "http://loinc.org", null)]
    [InlineData("http://loinc.org|2.77", "http://loinc.org", "2.77")]
    [InlineData("http://loinc.org|", "http://loinc.org", null)]
    [InlineData("http://example.org/a|b|1.0", "http://example.org/a|b", "1.0")]
    public void GivenCanonicalString_WhenParsed_ThenUrlAndVersionAreSplitOnLastBar(string input, string url, string? version)
    {
        var canonical = CanonicalRef.Parse(input);

        canonical.Url.ShouldBe(url);
        canonical.Version.ShouldBe(version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("|1.0")]
    public void GivenBlankUrl_WhenCanonicalCreated_ThenThrows(string input)
    {
        Should.Throw<ArgumentException>(() => CanonicalRef.Parse(input));
    }

    [Theory]
    [InlineData("", "a")]
    [InlineData("http://s", "")]
    [InlineData(" ", " ")]
    public void GivenBlankSystemOrCode_WhenCodeRefCreated_ThenThrows(string system, string code)
    {
        Should.Throw<ArgumentException>(() => new CodeRef(system, code));
    }

    [Fact]
    public void GivenCodeRefWithVersion_WhenAskingForItsCodeSystem_ThenCanonicalCarriesTheVersion()
    {
        var code = new CodeRef("http://snomed.info/sct", "22298006", "http://snomed.info/sct/900000000000207008");

        code.CodeSystem.ShouldBe(new CanonicalRef("http://snomed.info/sct", "http://snomed.info/sct/900000000000207008"));
    }

    [Fact]
    public void GivenBlankVersion_WhenCodeRefCreated_ThenVersionIsNull()
    {
        new CodeRef("http://s", "a", " ").Version.ShouldBeNull();
    }

    [Theory]
    [InlineData("http://example.org/vs", null, "http://example.org/vs")]
    [InlineData("http://example.org/vs", "1.0", "http://example.org/vs|1.0")]
    public void GivenCanonicalReference_WhenFormatted_ThenUrlAndOptionalVersionArePreserved(
        string url,
        string? version,
        string expected)
    {
        new CanonicalRef(url, version).ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("http://example.org/cs", "car", null, "http://example.org/cs#car")]
    [InlineData("http://example.org/cs", "car", "2026", "http://example.org/cs|2026#car")]
    public void GivenCodeReference_WhenFormatted_ThenCanonicalSystemAndCodeArePreserved(
        string system,
        string code,
        string? version,
        string expected)
    {
        new CodeRef(system, code, version).ToString().ShouldBe(expected);
    }

    [Fact]
    public void GivenUndeterminedMembership_WhenInspected_ThenResultIsUnknownAndReasonIsKept()
    {
        var reason = new UnknownReason(UnknownKind.SystemNotAvailable, "http://s", "not loaded");

        var membership = MembershipResult.Undetermined(reason);

        membership.Result.ShouldBe(Determination.Unknown);
        membership.Unknown.ShouldBe(reason);
        MembershipResult.Member.Unknown.ShouldBeNull();
        MembershipResult.NotMember.Result.ShouldBe(Determination.False);
    }

    [Fact]
    public void GivenNullReason_WhenUndeterminedConstructed_ThenThrows()
    {
        Should.Throw<ArgumentNullException>(() => new SubsumesOutcome.Undetermined((UnknownReason)null!)).ParamName.ShouldBe("reason");
        Should.Throw<ArgumentNullException>(() => new SubsumesOutcome.Undetermined((IReadOnlyList<UnknownReason>)null!))
            .ParamName.ShouldBe("reasons");
        Should.Throw<ArgumentException>(() => new SubsumesOutcome.Undetermined(Array.Empty<UnknownReason>()))
            .ParamName.ShouldBe("reasons");
        Should.Throw<ArgumentException>(() => new SubsumesOutcome.Undetermined([null!]))
            .ParamName.ShouldBe("reasons");
    }

    [Fact]
    public void GivenSeveralReasons_WhenUndeterminedConstructed_ThenAllReasonsAreKeptInOrder()
    {
        // Keeping only one reason hides why the other sources could not answer.
        var first = new UnknownReason(UnknownKind.UnsupportedFilter, "http://s", "no hierarchy");
        var second = new UnknownReason(UnknownKind.CodeNotInSystem, "http://s#x", "no such code");

        var outcome = new SubsumesOutcome.Undetermined([first, second]);

        outcome.Reasons.ShouldBe([first, second]);
        outcome.Reason.ShouldBe(first);
        outcome.Unknown.ShouldBe(first);
        outcome.ShouldBe(new SubsumesOutcome.Undetermined([first, second]));
        outcome.ShouldNotBe(new SubsumesOutcome.Undetermined(first));
        new SubsumesOutcome.Undetermined(first).Reasons.ShouldBe([first]);
    }

    [Fact]
    public void GivenReasonCollections_WhenCastToMutableTypes_ThenTheyCannotBeMutated()
    {
        // A caller that casts the exposed list must not be able to rewrite the reasons behind equality.
        var first = new UnknownReason(UnknownKind.UnsupportedFilter, "http://s", "no hierarchy");
        var second = new UnknownReason(UnknownKind.CodeNotInSystem, "http://s#x", "no such code");
        var other = new UnknownReason(UnknownKind.SourceFailed, "http://s#y", "boom");
        List<UnknownReason> source = [first, second];

        var exposed = new IReadOnlyList<UnknownReason>[]
        {
            ConceptLookup.Undetermined(first).Unknowns,
            ConceptLookup.Undetermined(source).Unknowns,
            ConceptLookup.Found(new ConceptDetails("a", null, "A", null, null, null)).Unknowns,
            ConceptLookup.NotFound.Unknowns,
            new SubsumesOutcome.Undetermined(first).Reasons,
            new SubsumesOutcome.Undetermined(source).Reasons,
            new SubsumesOutcome.Related(SubsumptionRelation.Subsumes).Unknowns,
            MembershipResult.Member.Unknowns,
            MembershipResult.NotMember.Unknowns,
            MembershipResult.Undetermined(first).Unknowns,
        };

        foreach (var reasons in exposed)
        {
            reasons.ShouldNotBeOfType<UnknownReason[]>();
            reasons.ShouldNotBeOfType<List<UnknownReason>>();
            var list = reasons.ShouldBeAssignableTo<IList<UnknownReason>>();
            list.IsReadOnly.ShouldBeTrue();
            if (list.Count > 0)
            {
                Should.Throw<NotSupportedException>(() => list[0] = other);
            }

            Should.Throw<NotSupportedException>(() => list.Add(other));
        }

        var lookup = ConceptLookup.Undetermined(source);
        var outcome = new SubsumesOutcome.Undetermined(source);
        source[0] = other;
        source.Add(other);

        lookup.Unknowns.ShouldBe([first, second]);
        outcome.Reasons.ShouldBe([first, second]);
    }

    [Fact]
    public void GivenSeveralReasons_WhenHashed_ThenEveryOrderedReasonContributesConsistentlyWithEquality()
    {
        var first = new UnknownReason(UnknownKind.UnsupportedFilter, "http://s", "no hierarchy");
        var second = new UnknownReason(UnknownKind.CodeNotInSystem, "http://s#x", "no such code");
        var third = new UnknownReason(UnknownKind.SourceFailed, "http://s#y", "boom");

        ConceptLookup.Undetermined([first, second]).GetHashCode()
            .ShouldBe(ConceptLookup.Undetermined([first, new UnknownReason(second.Kind, second.Subject, second.Detail)]).GetHashCode());
        new SubsumesOutcome.Undetermined([first, second]).GetHashCode()
            .ShouldBe(new SubsumesOutcome.Undetermined([first, new UnknownReason(second.Kind, second.Subject, second.Detail)]).GetHashCode());

        ConceptLookup.Undetermined([first, second]).GetHashCode()
            .ShouldNotBe(ConceptLookup.Undetermined([first, third]).GetHashCode());
        ConceptLookup.Undetermined([first, second]).GetHashCode()
            .ShouldNotBe(ConceptLookup.Undetermined([second, first]).GetHashCode());
        new SubsumesOutcome.Undetermined([first, second]).GetHashCode()
            .ShouldNotBe(new SubsumesOutcome.Undetermined([first, third]).GetHashCode());
        new SubsumesOutcome.Undetermined([first, second]).GetHashCode()
            .ShouldNotBe(new SubsumesOutcome.Undetermined([second, first]).GetHashCode());
    }

    [Theory]
    [InlineData(SubsumptionRelation.Subsumes, "subsumes")]
    [InlineData(SubsumptionRelation.SubsumedBy, "subsumed-by")]
    [InlineData(SubsumptionRelation.NotSubsumed, "not-subsumed")]
    public void GivenRelation_WhenMappedToFhir_ThenR4CodeIsReturned(SubsumptionRelation relation, string code)
    {
        relation.ToFhirCode().ShouldBe(code);
    }

    [Fact]
    public void GivenSubsumesOutcomes_WhenInspected_ThenExactlyOneOfRelationOrUnknownIsSet()
    {
        var reason = new UnknownReason(UnknownKind.CodeNotInSystem, "http://s#x", "no such code");

        SubsumesOutcome related = new SubsumesOutcome.Related(SubsumptionRelation.Subsumes);
        SubsumesOutcome undetermined = new SubsumesOutcome.Undetermined(reason);

        related.KnownRelation.ShouldBe(SubsumptionRelation.Subsumes);
        related.Unknown.ShouldBeNull();
        undetermined.KnownRelation.ShouldBeNull();
        undetermined.Unknown.ShouldBe(reason);
    }

    [Fact]
    public void GivenUndefinedRelation_WhenRelatedConstructed_ThenThrows()
    {
        var invalidRelation = (SubsumptionRelation)int.MaxValue;

        Should.Throw<ArgumentOutOfRangeException>(() => new SubsumesOutcome.Related(invalidRelation));
    }

    [Theory]
    [InlineData("", "detail")]
    [InlineData("subject", "")]
    public void GivenBlankSubjectOrDetail_WhenUnknownReasonCreated_ThenThrows(string subject, string detail)
    {
        Should.Throw<ArgumentException>(() => new UnknownReason(UnknownKind.SourceFailed, subject, detail));
    }
}
