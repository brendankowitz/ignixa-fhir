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
}
