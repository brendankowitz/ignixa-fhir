using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.Tests.Fixtures;

namespace Ignixa.DataLayer.SqlServer.Tests.Indexing;

/// <summary>
/// Mechanics of the bounded negative cache backing the reference-data cache's read-only lookups: a recorded
/// miss must expire on its own (nothing in process can observe a row another process created), must never
/// grow without limit, and must be droppable the instant a writer falsifies it.
/// </summary>
public class NegativeLookupCacheTests
{
    private const string Key = "http://example.org/CodeSystem/absent";

    [Fact]
    public void GivenAKeyNeverRecorded_WhenAskedWhetherItIsMissing_ThenItSaysNo()
    {
        // Arrange
        var cache = new NegativeLookupCache(new TestTimeProvider());

        // Act
        var known = cache.IsKnownMissing(Key);

        // Assert
        known.ShouldBeFalse("an unrecorded key means 'ask the database', which is what false says");
    }

    [Fact]
    public void GivenARecordedMiss_WhenAskedWithinItsLifetime_ThenItSaysYes()
    {
        // Arrange
        var time = new TestTimeProvider();
        var cache = new NegativeLookupCache(time, TimeSpan.FromMinutes(5));
        cache.RecordMiss(Key);

        // Act
        time.Advance(TimeSpan.FromMinutes(4));

        // Assert
        cache.IsKnownMissing(Key).ShouldBeTrue();
    }

    [Fact]
    public void GivenARecordedMiss_WhenItsLifetimeHasElapsed_ThenItSaysNoAndDropsTheEntry()
    {
        // Arrange: the TTL is the only thing that can recover from a row created by another process, so
        // expiry is the load-bearing behaviour here, not a housekeeping detail.
        var time = new TestTimeProvider();
        var cache = new NegativeLookupCache(time, TimeSpan.FromMinutes(5));
        cache.RecordMiss(Key);

        // Act
        time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromTicks(1));

        // Assert
        cache.IsKnownMissing(Key).ShouldBeFalse();
        cache.Count.ShouldBe(0, "reading an expired entry must also reclaim it");
    }

    [Fact]
    public void GivenARecordedMiss_WhenForgotten_ThenItSaysNoImmediately()
    {
        // Arrange
        var time = new TestTimeProvider();
        var cache = new NegativeLookupCache(time, TimeSpan.FromMinutes(5));
        cache.RecordMiss(Key);

        // Act
        cache.Forget(Key);

        // Assert
        cache.IsKnownMissing(Key).ShouldBeFalse(
            "a writer that created the row must be able to falsify the record without waiting for the TTL");
        cache.Count.ShouldBe(0);
    }

    [Fact]
    public void GivenAKeyNeverRecorded_WhenForgotten_ThenNothingHappens()
    {
        // Arrange
        var cache = new NegativeLookupCache(new TestTimeProvider());

        // Act
        var forget = () => cache.Forget(Key);

        // Assert
        forget.ShouldNotThrow();
        cache.Count.ShouldBe(0);
    }

    [Fact]
    public void GivenMissesRecordedUpToCapacity_WhenMoreAreRecorded_ThenTheEntryCountStaysBounded()
    {
        // Arrange: a caller enumerating distinct systems (?identifier=urn:x:{n}|a in a loop) must not be
        // able to grow this without limit.
        var time = new TestTimeProvider();
        var cache = new NegativeLookupCache(time, TimeSpan.FromMinutes(5), capacity: 10);

        // Act
        for (var i = 0; i < 500; i++)
        {
            cache.RecordMiss($"urn:example:{i}");
        }

        // Assert
        cache.Count.ShouldBeLessThanOrEqualTo(10);
    }

    [Fact]
    public void GivenACapacityBreachWithExpiredEntriesPresent_WhenAnotherMissIsRecorded_ThenExpiredEntriesAreReclaimedAndUnexpiredOnesSurvive()
    {
        // Arrange: eviction prefers reclaiming what is already stale over discarding live entries.
        var time = new TestTimeProvider();
        var cache = new NegativeLookupCache(time, TimeSpan.FromMinutes(5), capacity: 3);
        cache.RecordMiss("urn:stale:1");
        cache.RecordMiss("urn:stale:2");
        time.Advance(TimeSpan.FromMinutes(6));
        cache.RecordMiss("urn:fresh:1");
        cache.RecordMiss("urn:fresh:2");

        // Act
        cache.RecordMiss("urn:fresh:3");

        // Assert
        cache.IsKnownMissing("urn:stale:1").ShouldBeFalse();
        cache.IsKnownMissing("urn:stale:2").ShouldBeFalse();
        cache.IsKnownMissing("urn:fresh:3").ShouldBeTrue();
        cache.Count.ShouldBeLessThanOrEqualTo(3);
    }

    [Fact]
    public void GivenARecordedMiss_WhenRecordedAgainLater_ThenItsLifetimeIsExtendedFromTheLatestRecord()
    {
        // Arrange
        var time = new TestTimeProvider();
        var cache = new NegativeLookupCache(time, TimeSpan.FromMinutes(5));
        cache.RecordMiss(Key);

        // Act
        time.Advance(TimeSpan.FromMinutes(4));
        cache.RecordMiss(Key);
        time.Advance(TimeSpan.FromMinutes(2));

        // Assert
        cache.IsKnownMissing(Key).ShouldBeTrue("the second record restarts the lifetime, it does not stack");
        cache.Count.ShouldBe(1, "re-recording the same key must not add a second entry");
    }

    [Fact]
    public void GivenKeysDifferingOnlyByCase_WhenOneIsRecorded_ThenTheOtherIsUnaffected()
    {
        // Arrange: the positive caches compare ordinally, so this one must too or the two would disagree
        // about which spelling was probed.
        var cache = new NegativeLookupCache(new TestTimeProvider());

        // Act
        cache.RecordMiss("http://loinc.org");

        // Assert
        cache.IsKnownMissing("http://LOINC.ORG").ShouldBeFalse();
    }

    [Fact]
    public void GivenACapacityBelowOne_WhenConstructed_ThenItThrows()
    {
        // Arrange
        var construct = () => new NegativeLookupCache(new TestTimeProvider(), TimeSpan.FromMinutes(5), capacity: 0);

        // Act & Assert
        construct.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
