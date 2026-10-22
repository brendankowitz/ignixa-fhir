using Ignixa.Validation.Abstractions.Terminology;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Terminology;

public class TerminologyRequestTypeTests
{
    [Fact]
    public void GivenCodesFromDifferentSystems_WhenSubsumesRequestCreated_ThenThrows()
    {
        Should.Throw<ArgumentException>(() => new SubsumesRequest(new CodeRef("http://a", "x"), new CodeRef("http://b", "y")));
    }

    [Fact]
    public void GivenCodesWithDifferentVersions_WhenSubsumesRequestCreated_ThenThrows()
    {
        Should.Throw<ArgumentException>(() => new SubsumesRequest(new CodeRef("http://a", "x", "1"), new CodeRef("http://a", "y", "2")));
    }

    [Fact]
    public void GivenCodesFromOneSystemVersion_WhenSubsumesRequestCreated_ThenCodeSystemIsShared()
    {
        var request = new SubsumesRequest(new CodeRef("http://a", "x", "1"), new CodeRef("http://a", "y", "1"));

        request.CodeSystem.ShouldBe(new CanonicalRef("http://a", "1"));
    }

    [Fact]
    public void GivenLookupFactories_WhenInspected_ThenResultConceptAndUnknownAgree()
    {
        var reason = new UnknownReason(UnknownKind.SystemNotAvailable, "http://a", "not loaded");
        var details = new ConceptDetails(null, "1", "Car", null, null, null);

        ConceptLookup.Found(details).Result.ShouldBe(Determination.True);
        ConceptLookup.Found(details).Concept.ShouldBe(details);
        ConceptLookup.NotFound.Result.ShouldBe(Determination.False);
        ConceptLookup.NotFound.Concept.ShouldBeNull();
        ConceptLookup.Undetermined(reason).Result.ShouldBe(Determination.Unknown);
        ConceptLookup.Undetermined(reason).Unknown.ShouldBe(reason);
    }

    [Fact]
    public void GivenNullConcept_WhenFoundCreated_ThenThrowsForThatArgument()
    {
        Should.Throw<ArgumentNullException>(() => ConceptLookup.Found(null!)).ParamName.ShouldBe("concept");
    }

    [Fact]
    public void GivenNullReason_WhenUndeterminedLookupCreated_ThenThrowsForThatArgument()
    {
        Should.Throw<ArgumentNullException>(() => ConceptLookup.Undetermined((UnknownReason)null!)).ParamName.ShouldBe("reason");
    }

    [Fact]
    public void GivenSeveralReasons_WhenUndeterminedLookupCreated_ThenAllReasonsAreKeptInOrder()
    {
        // Keeping only one reason hides why the other sources could not answer.
        var first = new UnknownReason(UnknownKind.NonEnumerableSystem, "http://a", "subset");
        var second = new UnknownReason(UnknownKind.SystemVersionNotAvailable, "http://a|2", "not loaded");

        var lookup = ConceptLookup.Undetermined([first, second]);

        lookup.Result.ShouldBe(Determination.Unknown);
        lookup.Unknowns.ShouldBe([first, second]);
        lookup.Unknown.ShouldBe(first);
        lookup.ShouldBe(ConceptLookup.Undetermined([first, second]));
        lookup.ShouldNotBe(ConceptLookup.Undetermined(first));
    }

    [Fact]
    public void GivenDeterminedLookups_WhenInspected_ThenNoReasonsAreCarried()
    {
        ConceptLookup.NotFound.Unknowns.ShouldBeEmpty();
        ConceptLookup.Found(new ConceptDetails(null, null, "Car", null, null, null)).Unknowns.ShouldBeEmpty();
        ConceptLookup.Undetermined(new UnknownReason(UnknownKind.SystemNotAvailable, "http://a", "x"))
            .Unknowns.ShouldHaveSingleItem();
    }

    [Fact]
    public void GivenNoReasons_WhenUndeterminedLookupCreated_ThenThrows()
    {
        // An unknown answer without a reason cannot be explained to the caller.
        Should.Throw<ArgumentNullException>(() => ConceptLookup.Undetermined((IReadOnlyList<UnknownReason>)null!))
            .ParamName.ShouldBe("reasons");
        Should.Throw<ArgumentException>(() => ConceptLookup.Undetermined(Array.Empty<UnknownReason>()))
            .ParamName.ShouldBe("reasons");
        Should.Throw<ArgumentException>(() => ConceptLookup.Undetermined([null!]))
            .ParamName.ShouldBe("reasons");
    }
}
