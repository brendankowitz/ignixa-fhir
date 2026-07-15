// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class BundleFacadeTests
{
    [Fact]
    public void GivenBundle_WhenReadBack_ThenSharedFieldsRoundTrip()
    {
        var bundle = new Bundle
        {
            Id = "example",
            Total = 1,
        };
        bundle.SetTypeRaw("searchset");

        bundle.Id.ShouldBe("example");
        bundle.Total.ShouldBe(1);
        bundle.GetTypeRaw().ShouldBe("searchset");
    }

    [Fact]
    public void GivenBundle_WhenSetTypeRawCalledWithR5OnlyLiteral_ThenValueRoundTrips()
    {
        var bundle = new Bundle();

        bundle.SetTypeRaw("subscription-notification");

        bundle.GetTypeRaw().ShouldBe("subscription-notification");
    }

    [Fact]
    public void GivenBundleEntry_WhenReadBack_ThenResourceAndRequestRoundTrip()
    {
        var bundle = new Bundle();
        var entry = new BundleEntry
        {
            FullUrl = "urn:uuid:123",
            Request = new BundleEntryRequest
            {
                Method = HttpVerb.PUT,
                Url = "Patient/123",
            },
        };
        bundle.Entry.Add(entry);

        bundle.Entry.Single().FullUrl.ShouldBe("urn:uuid:123");
        bundle.Entry.Single().Request!.Method.ShouldBe(HttpVerb.PUT);
        bundle.Entry.Single().Request!.Url.ShouldBe("Patient/123");
    }

    [Fact]
    public void GivenBundleEntrySearch_WhenModeSet_ThenTypedEnumRoundTrips()
    {
        var entry = new BundleEntry
        {
            Search = new BundleEntrySearch { Mode = SearchEntryMode.Include },
        };

        entry.Search!.Mode.ShouldBe(SearchEntryMode.Include);
    }

    [Fact]
    public void GivenBundleEntryResponse_WhenLastModifiedOffsetSet_ThenRawStringRoundTrips()
    {
        var response = new BundleEntryResponse();
        var timestamp = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

        response.LastModifiedOffset = timestamp;

        response.LastModifiedOffset.ShouldBe(timestamp);
        response.LastModified.ShouldBe("2026-07-14T12:00:00.0000000+00:00");
    }

    [Fact]
    public void GivenBundleEntryResponse_WhenLastModifiedNotSet_ThenLastModifiedOffsetIsNull()
    {
        var response = new BundleEntryResponse();

        response.LastModifiedOffset.ShouldBeNull();
    }

    [Fact]
    public void GivenBundleLink_WhenSetRelationRawCalled_ThenValueRoundTrips()
    {
        var link = new BundleLink { Url = "https://example.org/next" };

        link.SetRelationRaw("next");

        link.GetRelationRaw().ShouldBe("next");
        link.Url.ShouldBe("https://example.org/next");
    }

    [Fact]
    public void GivenBundleLink_WhenGetRelationRawCalledWithoutSetting_ThenReturnsNull()
    {
        var link = new BundleLink();

        link.GetRelationRaw().ShouldBeNull();
    }
}
