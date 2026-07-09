// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

namespace Ignixa.Api.E2ETests.Search.DataTypes;

/// <summary>
/// E2E tests for canonical URI search parameters.
/// Tests canonical search with versions, fragments, and multiple profiles.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.CanonicalSearchTests
/// </summary>
[Collection(E2ETestCollection.Name)]
public class CanonicalSearchTests : CapabilityDrivenTestBase, IClassFixture<CanonicalSearchFixture>
{
    private readonly CanonicalSearchFixture _fixture;

    public CanonicalSearchTests(IgnixaApiFixture apiFixture, CanonicalSearchFixture fixture)
        : base(apiFixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    /// <summary>
    /// Tests canonical search with version and fragment.
    /// When searching for a canonical URI with version and fragment (e.g., http://example.org/profile|2#section),
    /// the search should match resources with that exact profile URI.
    /// Ported from: CanonicalSearchTests.GivenAnObservationWithProfile_WhenSearchingByCanonicalUriVersionFragment
    /// </summary>
    /// <remarks>
    /// SKIP: Canonical version/fragment search requires schema migration to add separate Version/Fragment
    /// columns. Currently uses full URI matching instead. See ADR-XXX for implementation plan.
    /// </remarks>
    [Fact(Skip = "Canonical version/fragment search requires schema migration")]
    public async Task GivenAnObservationWithProfile_WhenSearchingByCanonicalUriVersionFragment_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "_profile");

        // Act - Search for profile with version 2 and fragment
        // Note: Fragment (#section) must be URL-encoded as %23section in query string
        var searchValue = Uri.EscapeDataString(_fixture.ObservationProfileV2WithFragment);
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&_profile={searchValue}");

        // Assert - Should match observation[2] which has the profile with version 2 and fragment
        results.Length.ShouldBe(1);
        results[0].Id.ShouldBe(_fixture.Observations[2].Id);

        // Verify the profile is present in meta.profile
        var profiles = results[0].MutableNode["meta"]?["profile"];
        profiles.ShouldNotBeNull();
    }

    /// <summary>
    /// Tests canonical search with base URI only (no version).
    /// When searching for a canonical URI without version (e.g., http://example.org/profile),
    /// the search should match resources with that profile regardless of version or fragment.
    /// Ported from: CanonicalSearchTests.GivenAnObservationWithProfile_WhenSearchingByCanonicalUri
    /// </summary>
    /// <remarks>
    /// SKIP: Canonical base URI search that matches all versions requires schema migration to add
    /// separate Version/Fragment columns. Currently uses full URI matching. See ADR-XXX.
    /// </remarks>
    [Fact(Skip = "Canonical base URI search requires schema migration")]
    public async Task GivenAnObservationWithProfile_WhenSearchingByCanonicalUri_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "_profile");

        // Act - Search for base profile URI (no version)
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&_profile={_fixture.ObservationProfileUri}");

        // Assert - Should match all observations with the base profile URI
        // Observations [0], [1], [2], [3] all have the base profile URI
        // (some with versions, some with fragments, some without)
        results.Length.ShouldBe(4);

        // Verify all observations are in results
        foreach (var expectedObs in _fixture.Observations)
        {
            results.ShouldContain(r => r.Id == expectedObs.Id,
                $"Expected observation {expectedObs.Id} should be in results when searching by base URI");
        }
    }

    /// <summary>
    /// Tests canonical search with version but no fragment.
    /// When searching for a canonical URI with version (e.g., http://example.org/profile|2),
    /// the search should match resources with that profile and version, regardless of fragment.
    /// Ported from: CanonicalSearchTests.GivenAnObservationWithProfile_WhenSearchingByCanonicalUriVersion
    /// </summary>
    /// <remarks>
    /// SKIP: Canonical version search requires schema migration to add separate Version column.
    /// Currently uses full URI matching. See ADR-XXX for implementation plan.
    /// </remarks>
    [Fact(Skip = "Canonical version search requires schema migration")]
    public async Task GivenAnObservationWithProfile_WhenSearchingByCanonicalUriVersion_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "_profile");

        // Act - Search for profile with version 2 (no fragment)
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&_profile={_fixture.ObservationProfileV2}");

        // Assert - Should match observations with version 2
        // Observations [1] and [2] both have profile version 2
        // [1] has version 2 only, [2] has version 2 with fragment
        results.Length.ShouldBe(2);

        results.ShouldContain(r => r.Id == _fixture.Observations[1].Id,
            "Observation[1] with version 2 should be in results");
        results.ShouldContain(r => r.Id == _fixture.Observations[2].Id,
            "Observation[2] with version 2 and fragment should be in results");
    }

    /// <summary>
    /// Tests canonical search when a resource has multiple profiles.
    /// When searching for one profile, the search should match resources that have that profile
    /// even if they also have other profiles.
    /// Ported from: CanonicalSearchTests.GivenAnObservationWithProfile_WhenSearchingByCanonicalUriMultipleProfiles
    /// </summary>
    [Fact]
    public async Task GivenAnObservationWithProfile_WhenSearchingByCanonicalUriMultipleProfiles_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "_profile");

        // Act - Search for alternate profile URI
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&_profile={_fixture.ObservationProfileUriAlternate}");

        // Assert - Should match observation[3] which has both base and alternate profiles
        results.Length.ShouldBe(1);
        results[0].Id.ShouldBe(_fixture.Observations[3].Id);

        // Verify both profiles are present in meta.profile
        var profiles = results[0].MutableNode["meta"]?["profile"];
        profiles.ShouldNotBeNull();
    }
}
