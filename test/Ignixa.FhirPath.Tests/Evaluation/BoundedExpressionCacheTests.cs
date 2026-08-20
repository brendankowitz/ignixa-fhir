// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Evaluation;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Pins the properties the expression caches rely on: a bound that a caller supplying unbounded
/// distinct expressions cannot drive past, and a retained previous generation so that bound does not
/// turn into a re-parse on every write.
/// </summary>
public class BoundedExpressionCacheTests
{
    [Fact]
    public void GivenMoreDistinctKeysThanCapacity_WhenAdding_ThenTheHotGenerationStaysWithinCapacity()
    {
        // Arrange
        var cache = new BoundedExpressionCache<string>(capacity: 8);

        // Act
        for (var i = 0; i < 500; i++)
        {
            cache.GetOrAdd($"expression-{i}", static key => key);
        }

        // Assert
        cache.Count.ShouldBeLessThanOrEqualTo(8);
    }

    [Fact]
    public void GivenMoreDistinctKeysThanTwiceCapacity_WhenAdding_ThenHotPlusColdNeverExceedsTwiceCapacity()
    {
        // The cache's real memory contract is bounded at 2x capacity across both generations, not just
        // capacity on the hot one - Count alone (hot.Count <= capacity by construction of Store/Rotate)
        // says nothing about the cold generation, which is where a leak would actually show up.

        // Arrange
        const int capacity = 8;
        var cache = new BoundedExpressionCache<string>(capacity: capacity);

        // Act & Assert - checked after every insertion, not just at the end, so a rotation that
        // momentarily (or permanently) lets both generations fill past capacity is caught.
        for (var i = 0; i < 500; i++)
        {
            cache.GetOrAdd($"expression-{i}", static key => key);

            var total = cache.Count + cache.ColdCount;
            total.ShouldBeLessThanOrEqualTo(capacity * 2);
        }
    }

    [Fact]
    public void GivenAKeyThatSurvivesOneRotationButIsNeverRefreshed_WhenASecondRotationCompletes_ThenItIsEvicted()
    {
        // A key demoted to the cold generation survives exactly one further rotation. If it is never
        // read (and thus never promoted back into hot) before a second rotation, the generation holding
        // it is discarded outright and the key is genuinely gone - the two-generation bound is a bound,
        // not an unbounded history. This is what distinguishes the cache from a plain LRU that never loses
        // an entry until it is truly stale.

        // Arrange
        const int capacity = 4;
        var cache = new BoundedExpressionCache<string>(capacity: capacity);
        cache.GetOrAdd("target", static key => key);

        // Act - one rotation: "target" moves from hot to cold, alongside the fillers that were hot
        // at the moment the rotation triggered.
        for (var i = 0; i < capacity; i++)
        {
            cache.GetOrAdd($"filler-a-{i}", static key => key);
        }
        cache.ColdCount.ShouldBe(capacity, "a rotation always demotes a full hot generation into cold");

        // A second rotation, still without ever reading "target" back: the cold generation holding it
        // is replaced wholesale by the generation that was hot going into this rotation.
        for (var i = 0; i < capacity; i++)
        {
            cache.GetOrAdd($"filler-b-{i}", static key => key);
        }

        var recomputed = false;
        var value = cache.GetOrAdd(
            "target",
            key =>
            {
                recomputed = true;
                return key;
            });

        // Assert
        value.ShouldBe("target");
        recomputed.ShouldBeTrue("a key that survives two full rotations without being re-read must fall out of both generations");
    }

    [Fact]
    public void GivenAKeyServedFromTheColdGeneration_WhenReadAgain_ThenItIsPromotedIntoTheHotGeneration()
    {
        // Being "served without recomputing" from cold is necessary but not sufficient: the promotion
        // has to actually write the entry back into hot, or it would be silently lost on the very next
        // rotation - a cache that just special-cased "return the cold value but never re-store it" would
        // pass a same-generation re-read test yet still lose the entry across generations. This pins the
        // promotion by forcing a second rotation and confirming the promoted key survives it.

        // Arrange
        const int capacity = 3;
        var cache = new BoundedExpressionCache<string>(capacity: capacity);
        cache.GetOrAdd("target", static key => key);
        for (var i = 0; i < capacity; i++)
        {
            cache.GetOrAdd($"filler-pre-{i}", static key => key);
        }
        cache.ColdCount.ShouldBe(capacity, "the rotation must have demoted \"target\" (and the fillers hot alongside it) into cold");

        // Act - promote "target" back into hot, then drive a full further generation of distinct keys
        // through the cache. If promotion actually wrote "target" into hot, it rides along into the next
        // cold generation when this rotates; if promotion only returned the value without re-storing it,
        // "target" is left behind in the generation this rotation discards.
        cache.GetOrAdd("target", static key => key);
        for (var i = 0; i < capacity; i++)
        {
            cache.GetOrAdd($"filler-post-{i}", static key => key);
        }

        var recomputed = false;
        var value = cache.GetOrAdd(
            "target",
            key =>
            {
                recomputed = true;
                return key;
            });

        // Assert
        value.ShouldBe("target");
        recomputed.ShouldBeFalse("a promoted key must survive the rotation that follows its promotion");
    }

    [Fact]
    public void GivenAKeyEvictedFromTheHotGeneration_WhenReadAgain_ThenItIsServedWithoutRecomputing()
    {
        // The cliff this cache exists to avoid. A plain clear-at-capacity would re-invoke the factory
        // here, which on the indexing path means re-parsing every search parameter on every write.

        // Arrange
        var cache = new BoundedExpressionCache<string>(capacity: 4);
        cache.GetOrAdd("first", static key => key);

        for (var i = 0; i < 4; i++)
        {
            cache.GetOrAdd($"filler-{i}", static key => key);
        }

        // Act
        var recomputed = false;
        var value = cache.GetOrAdd(
            "first",
            key =>
            {
                recomputed = true;
                return key;
            });

        // Assert
        value.ShouldBe("first");
        recomputed.ShouldBeFalse("an entry demoted to the cold generation must still be served from it");
    }

    [Fact]
    public void GivenACachedNullValue_WhenReadAgain_ThenItIsTreatedAsAHitRatherThanAMiss()
    {
        // The delegate cache stores null to mean "this expression has no compiled form", so a null must
        // not be re-derived on every call - that would run the delegate compiler on every evaluation of
        // every expression that falls back to the interpreter.

        // Arrange
        var cache = new BoundedExpressionCache<string?>(capacity: 8);
        cache.GetOrAdd("uncompilable", static _ => null);

        // Act
        var invocations = 0;
        cache.GetOrAdd(
            "uncompilable",
            _ =>
            {
                invocations++;
                return null;
            });

        // Assert
        invocations.ShouldBe(0);
    }

    [Fact]
    public void GivenAPopulatedCache_WhenCleared_ThenBothGenerationsAreDropped()
    {
        // Arrange
        var cache = new BoundedExpressionCache<string>(capacity: 4);
        for (var i = 0; i < 6; i++)
        {
            cache.GetOrAdd($"expression-{i}", static key => key);
        }

        // Act
        cache.Clear();

        // Assert
        cache.Count.ShouldBe(0);

        var recomputed = false;
        cache.GetOrAdd(
            "expression-0",
            key =>
            {
                recomputed = true;
                return key;
            });

        recomputed.ShouldBeTrue("Clear must drop the cold generation too, not just the hot one");
    }

    [Fact]
    public void GivenACapacityBelowOne_WhenConstructing_ThenItIsRejected()
    {
        // Arrange, Act, Assert
        Should.Throw<ArgumentOutOfRangeException>(() => new BoundedExpressionCache<string>(capacity: 0));
    }
}
