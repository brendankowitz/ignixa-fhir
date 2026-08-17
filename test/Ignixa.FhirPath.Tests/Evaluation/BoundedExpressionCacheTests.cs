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
