// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;

namespace Ignixa.Api.E2ETests.Search.Modifiers;

/// <summary>
/// E2E tests pinning FHIR R4's SHALL-reject rule for a search parameter suffixed by a modifier the
/// server does not support (https://hl7.org/fhir/R4/search.html#modifiers).
/// </summary>
/// <remarks>
/// This is distinct from an unsupported/unknown *parameter*, which R4 only says SHOULD be ignored and
/// which this server only rejects when the client opts in via <c>Prefer: handling=strict</c>
/// (see <see cref="Sorting.SortTests.GivenPatients_WhenSearchedWithInvalidSortAndHandlingStrict_ThenBadRequestReturned"/>).
/// An unsupported modifier must be rejected unconditionally -- silently dropping it would widen the
/// result set instead of narrowing it, which a client reading the entry list cannot detect.
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class UnsupportedModifierTests : CapabilityDrivenTestBase
{
    public UnsupportedModifierTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    [Theory]
    [InlineData("_id:above=abc")]
    [InlineData("_id:exact=abc")]
    [InlineData("_lastUpdated:above=2020")]
    public async Task GivenAnUnsupportedModifier_WhenSearchedWithNoPreferHeader_ThenBadRequestReturned(string queryString)
    {
        var response = await Client.GetAsync($"/Patient?{queryString}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAnUnsupportedModifier_WhenSearchedWithNoPreferHeader_ThenTheDiagnosticsNameTheModifierNotTheParameter()
    {
        // The SHALL-reject case must read differently from an ignored/unsupported *parameter* (which this
        // server only rejects when the client opts in via Prefer: handling=strict) -- a client parsing
        // OperationOutcome.issue[].diagnostics needs to be able to tell "your modifier was rejected" apart
        // from "your parameter was ignored," even though both currently map to the same severity/code.
        var response = await Client.GetAsync("/Patient?_id:above=abc");
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("uses a modifier that is not supported");
    }

    [Fact]
    public async Task GivenMultipleUnsupportedModifiers_WhenSearchedWithNoPreferHeader_ThenEveryOneIsReported()
    {
        // UnsupportedModifierParams is a list; this pins that the diagnostics loop reports every offending
        // parameter, not just the first one it finds.
        var response = await Client.GetAsync("/Patient?_id:above=abc&_lastUpdated:above=2020");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        body.ShouldContain("_id:above");
        body.ShouldContain("_lastUpdated:above");
    }

    [Fact]
    public async Task GivenASupportedModifierOnTheSameIntrinsicParameter_WhenSearchedWithNoPreferHeader_ThenNotRejected()
    {
        // Control: `_id:not` is a supported modifier on the same intrinsic parameter, so its presence
        // alone must not trigger the unsupported-modifier rejection.
        var response = await Client.GetAsync("/Patient?_id:not=abc");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
