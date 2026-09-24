// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Shouldly;

namespace Ignixa.Api.E2ETests.Search.Modifiers;

/// <summary>
/// E2E coverage for a modifier the server does not support on the parameter it is attached to.
/// </summary>
/// <remarks>
/// <para>
/// FHIR R4 separates this from an unknown search parameter. An unknown parameter SHOULD be ignored —
/// proxies inject parameters the client never sent, and the self link reports what was used. An
/// unsupported modifier is a SHALL: "Server SHALL reject any search request that ... is suffixed by a
/// modifier that the server does not support for that parameter ... using an HTTP 400 error".
/// </para>
/// <para>
/// The asymmetry is not pedantry. Ignoring an unknown parameter leaves the result set no wider than the
/// client asked for. Ignoring a modifier removes the filter entirely — <c>_id:above=abc</c> becomes an
/// unfiltered search over the whole resource type — and the client reading the entry list cannot tell
/// that from a filter that legitimately matched everything.
/// </para>
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class UnsupportedModifierHandlingTests : CapabilityDrivenTestBase
{
    public UnsupportedModifierHandlingTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    [Theory]
    [InlineData("_id:above=abc")]
    [InlineData("_id:exact=abc")]
    public async Task GivenAnUnsupportedModifierOnAnIntrinsicParameter_WhenSearchedWithNoPreferHeader_ThenBadRequestIsReturned(string queryString)
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, $"/Observation?{queryString}");

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAnUnsupportedModifierOnAnIntrinsicParameter_WhenSearchedWithHandlingStrict_ThenBadRequestIsReturned()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/Observation?_id:above=abc");
        request.Headers.Add("Prefer", "handling=strict");

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GivenAnUnsupportedModifierOnAnIntrinsicParameter_WhenSearchedWithHandlingLenient_ThenTheParameterIsIgnored()
    {
        // Arrange -- R4 lets the client ask the server to ignore what it could not honour, and the server
        // SHOULD honour that request. This is the only way to get the pre-rejection behaviour back.
        var request = new HttpRequestMessage(HttpMethod.Get, "/Observation?_id:above=abc");
        request.Headers.Add("Prefer", "handling=lenient");

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenASupportedModifierOnTheSameParameter_WhenSearchedWithNoPreferHeader_ThenTheSearchStillSucceeds()
    {
        // Arrange -- the control. Without it, "_id:above is a 400" would pass equally well if _id had been
        // broken outright; _id:not is the modifier the server does support on the same parameter.
        var request = new HttpRequestMessage(HttpMethod.Get, "/Observation?_id:not=abc");

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // The other half of the classification -- that an unknown parameter is NOT swept into the rejection --
    // is pinned in UnsupportedModifierClassificationTests rather than here. It cannot be asserted end to
    // end today: CompositeSearchParameterDefinitionManager.GetSearchParameter, the implementation this
    // pipeline actually resolves through, throws InvalidOperationException for an unknown code where
    // SearchParameterDefinitionManager throws SearchParameterNotSupportedException. Only the latter is
    // caught in SearchOptionsBuilder, so an unknown parameter already escapes as a 400 before any handling
    // preference is consulted. That is a separate pre-existing defect, untouched here.
}
