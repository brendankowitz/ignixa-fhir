// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class MetaFacadeTests
{
    [Fact]
    public void GivenMetaWithVersionIdAndSource_WhenReadBack_ThenValuesRoundTrip()
    {
        var meta = new Meta
        {
            VersionId = "1",
            Source = "http://example.org/source",
        };

        meta.VersionId.ShouldBe("1");
        meta.Source.ShouldBe("http://example.org/source");
        meta.MutableNode()["versionId"]!.GetValue<string>().ShouldBe("1");
    }

    [Fact]
    public void GivenMetaWithProfileAndTag_WhenReadBack_ThenListsAreSpecCorrect()
    {
        var meta = new Meta();
        meta.Profile.Add("http://example.org/StructureDefinition/foo");
        meta.Tag.Add(new Coding { System = "http://example.org/tags", Code = "test" });

        meta.Profile.Single().ShouldBe("http://example.org/StructureDefinition/foo");
        meta.Tag.Single().Code.ShouldBe("test");
    }

    [Fact]
    public void GivenLastUpdatedOffset_WhenSet_ThenLastUpdatedIsIso8601Utc()
    {
        var meta = new Meta
        {
            LastUpdatedOffset = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero),
        };

        meta.LastUpdated.ShouldBe("2026-07-13T12:00:00.0000000+00:00");
    }

    [Fact]
    public void GivenLastUpdatedOffsetWithNonUtcOffset_WhenSet_ThenReadBackConvertsToUtc()
    {
        var meta = new Meta
        {
            LastUpdatedOffset = new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.FromHours(-4)),
        };

        meta.LastUpdatedOffset!.Value.Offset.ShouldBe(TimeSpan.Zero);
        meta.LastUpdatedOffset!.Value.ShouldBe(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GivenNoLastUpdated_WhenReadingLastUpdatedOffset_ThenReturnsNull()
    {
        var meta = new Meta();

        meta.LastUpdatedOffset.ShouldBeNull();
    }

    [Fact]
    public void GivenLastUpdatedOffsetSetToNull_WhenReadBack_ThenLastUpdatedElementIsRemoved()
    {
        var meta = new Meta { LastUpdatedOffset = DateTimeOffset.UtcNow };

        meta.LastUpdatedOffset = null;

        meta.LastUpdated.ShouldBeNull();
    }
}
