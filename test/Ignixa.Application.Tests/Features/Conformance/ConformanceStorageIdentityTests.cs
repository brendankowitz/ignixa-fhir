using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Shouldly;
using Xunit;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Conformance;

public class ConformanceStorageIdentityTests
{
    [Fact]
    public void GivenUnanchoredSelfOverride_WhenReplayed_ThenItFailsWithoutPublishingTheParameter()
    {
        using var state = new ConformanceState();
        const string canonical = "http://example.org/SearchParameter/self";

        var error = Should.Throw<InvalidOperationException>(() => state.Apply(
            Activation(1, canonical, "identifier", 7, new OverrideInfo(canonical, 7))));

        error.InnerException!.Message.ShouldContain("self override");
        state.FindByCanonical(canonical).ShouldBeNull();
    }

    [Fact]
    public void GivenConflictingInheritedRoot_WhenReplayed_ThenItFailsWithoutChangingTheCurrentParameter()
    {
        using var state = new ConformanceState();
        const string first = "http://example.org/SearchParameter/first";
        const string second = "http://example.org/SearchParameter/second";
        const string conflicting = "http://example.org/SearchParameter/conflicting";
        state.Apply(Activation(1, first, "identifier", 1, null));
        state.Apply(Activation(2, second, "other", 2, null));

        var error = Should.Throw<InvalidOperationException>(() => state.Apply(
            Activation(3, conflicting, "identifier", 1, new OverrideInfo(second, 1))));

        error.InnerException!.Message.ShouldContain("inherited ID");
        state.GetSearchParameter("Patient", "identifier")!.Canonical.ShouldBe(first);
        state.FindByCanonical(conflicting).ShouldBeNull();
    }

    private static SourceEvent Activation(long eventId, string canonical, string code, int id, OverrideInfo? overrides) =>
        new(eventId, "identity-test", nameof(SearchParameterActivated),
            new SearchParameterActivated(canonical, code, "Patient", "Patient.identifier",
                SearchParamType.Token, "identity.test@1", overrides, id, null, null, null, null),
            DateTimeOffset.UtcNow);

}
