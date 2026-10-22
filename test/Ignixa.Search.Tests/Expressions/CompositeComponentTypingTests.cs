// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Tests.Indexing;

namespace Ignixa.Search.Tests.Expressions;

/// <summary>
/// Regression: the binder retyped a composite component by the shape of its value, so a String component whose value
/// held a <c>|</c> was bound as a token and one shaped like <c>Type/id</c> as a reference, and the search matched nothing.
/// Only a declared Reference or Token component is retyped (between the two) now.
/// </summary>
public class CompositeComponentTypingTests
{
    [Theory]
    [InlineData("code-value-string=x$a|b", "a|b")]
    [InlineData("code-value-string=x$Patient/1", "Patient/1")]
    public void GivenAStringComponentValueShapedLikeATokenOrReference_WhenSearched_ThenItIsMatchedAsAString(string query, string text)
    {
        string observation = $$"""{"resourceType":"Observation","id":"o1","status":"final","code":{"coding":[{"code":"x"}]},"valueString":"{{text}}"}""";

        VersionedCorpus.For(FhirVersion.R4).Match("Observation", query, observation).ShouldBe(["o1"]);
    }
}
