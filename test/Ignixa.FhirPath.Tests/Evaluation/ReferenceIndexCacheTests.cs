// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Unit tests for <see cref="ReferenceIndexCache"/>, the per-<see cref="EvaluationContext"/> cache
/// that <c>resolve()</c> uses so a Bundle/Parameters instance is only traversed once per evaluation
/// even though every element's <c>resolve()</c> call reaches the same root. Its root-identity guard
/// (<c>_index is not null &amp;&amp; ReferenceEquals(_root, root)</c>) is what stops a context whose root
/// was re-pointed at a different resource from resolving against the previous, now-stale, index.
/// </summary>
public class ReferenceIndexCacheTests
{
    private readonly IFhirSchemaProvider _r4Provider = FhirVersion.R4.GetSchemaProvider();

    private IElement ToElement(string json) =>
        ResourceJsonNode.Parse(json).ToElement(_r4Provider);

    [Fact]
    public void GivenSameRootPassedTwice_WhenGettingOrBuilding_ThenReturnsTheSameIndexAndTraversesOnlyOnce()
    {
        // Arrange - degrading the guard to `if (_index is not null) return _index;` (ignoring root
        // identity) would still pass this test, since the root does not change between calls; the
        // counting wrapper here proves the caching actually avoids a second traversal, which the
        // "different root" test below then uses to prove the guard also rebuilds when it must.
        var patient = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [
                { ""resourceType"": ""Practitioner"", ""id"": ""p1"" }
            ]
        }");
        var countingRoot = new ContainedCountingElement(patient);
        var cache = new ReferenceIndexCache();

        // Act
        var first = cache.GetOrBuild(countingRoot);
        var second = cache.GetOrBuild(countingRoot);

        // Assert
        second.ShouldBeSameAs(first);
        countingRoot.ContainedCallCount.ShouldBe(1);
    }

    [Fact]
    public void GivenADifferentRootOnTheSecondCall_WhenGettingOrBuilding_ThenRebuildsAndResolvesAgainstTheNewRoot()
    {
        // Arrange - this is the branch that is currently unpinned: degrading the guard to
        // `if (_index is not null) return _index;` makes GetOrBuild(rootB) return the stale index
        // built from rootA, so both assertions below fail under that mutation (verified manually;
        // see the task report).
        var rootA = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""patA"",
            ""contained"": [
                { ""resourceType"": ""Practitioner"", ""id"": ""shared"", ""name"": [ { ""family"": ""FromA"" } ] }
            ]
        }");
        var rootB = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""patB"",
            ""contained"": [
                { ""resourceType"": ""Practitioner"", ""id"": ""shared"", ""name"": [ { ""family"": ""FromB"" } ] }
            ]
        }");
        var cache = new ReferenceIndexCache();

        // Act
        var indexA = cache.GetOrBuild(rootA);
        var indexB = cache.GetOrBuild(rootB);

        // Assert
        indexB.ShouldNotBeSameAs(indexA);
        var resolved = indexB!.Resolve("#shared");
        resolved.ShouldNotBeNull();
        resolved!.Children("name").Single().Children("family").Single().Value.ShouldBe("FromB");
    }

    [Fact]
    public void GivenANullRoot_WhenGettingOrBuilding_ThenReturnsNull()
    {
        // Arrange
        var cache = new ReferenceIndexCache();

        // Act
        var result = cache.GetOrBuild(null);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenAContextCopiedViaPushThis_WhenReadingReferenceIndexCache_ThenTheSameCacheInstanceIsShared()
    {
        // Arrange - the whole reason resolve() only builds the in-instance index once per
        // evaluation is that ReferenceIndexCache survives record `with`-copies (PushThis, WithFocus,
        // etc.): the property is set once via its default initializer and never reassigned by those
        // copy helpers, so every derived context still points at the original cache instance.
        var context = new EvaluationContext();
        var element = new DummyElement();

        // Act
        var copiedContext = context.PushThis(element);

        // Assert
        copiedContext.ReferenceIndexCache.ShouldBeSameAs(context.ReferenceIndexCache);
    }

    [Fact]
    public void GivenTwoIndependentlyConstructedEvaluationContexts_WhenReadingReferenceIndexCache_ThenEachHasItsOwnDistinctCache()
    {
        // Arrange
        var first = new EvaluationContext();
        var second = new EvaluationContext();

        // Act & Assert - a genuinely new EvaluationContext() must not share the previous context's
        // cache; only `with`-derived copies of the SAME context do.
        second.ReferenceIndexCache.ShouldNotBeSameAs(first.ReferenceIndexCache);
    }

    /// <summary>
    /// Minimal <see cref="IElement"/> used only to prove <see cref="EvaluationContext.PushThis"/>
    /// preserves the <see cref="EvaluationContext.ReferenceIndexCache"/> instance; its own content is
    /// irrelevant to that assertion.
    /// </summary>
    private sealed class DummyElement : IElement
    {
        public string Name => string.Empty;
        public object? Value => null;
        public string InstanceType => "string";
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();

        public T? Meta<T>() where T : class => null;
    }

    /// <summary>
    /// Wraps a real, fully parsed <see cref="IElement"/> and counts how many times
    /// <c>Children("contained")</c> is invoked, so a test can prove <see cref="ReferenceIndex.Build"/>
    /// only traverses the instance once across repeated <see cref="ReferenceIndexCache.GetOrBuild"/>
    /// calls with the same root.
    /// </summary>
    private sealed class ContainedCountingElement : IElement
    {
        private readonly IElement _inner;

        public ContainedCountingElement(IElement inner)
        {
            _inner = inner;
        }

        public int ContainedCallCount { get; private set; }

        public string Name => _inner.Name;
        public object? Value => _inner.Value;
        public string InstanceType => _inner.InstanceType;
        public string Location => _inner.Location;
        public IType? Type => _inner.Type;
        public bool HasPrimitiveValue => _inner.HasPrimitiveValue;

        public IReadOnlyList<IElement> Children(string? name = null)
        {
            if (name == "contained")
            {
                ContainedCallCount++;
            }

            return _inner.Children(name);
        }

        public T? Meta<T>() where T : class => _inner.Meta<T>();
    }
}
