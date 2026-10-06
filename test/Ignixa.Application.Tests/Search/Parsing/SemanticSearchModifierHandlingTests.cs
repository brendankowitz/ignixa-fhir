// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Parsing;

/// <summary>
/// SearchOptionsBuilder-level regression coverage for Task 2 of the slice-1 plan (review finding: a
/// modifier -- or a chain -- on a semantic parameter must land in
/// <see cref="SearchOptions.UnsupportedModifierParams"/>, not merely in the weaker
/// <see cref="SearchOptions.UnsupportedParams"/>).
/// </summary>
/// <remarks>
/// <c>FhirEndpoints.CheckStrictHandling</c> rejects <see cref="SearchOptions.UnsupportedModifierParams"/>
/// with an HTTP 400 by default, no <c>Prefer</c> header required, per R4's modifier SHALL. It only
/// rejects <see cref="SearchOptions.UnsupportedParams"/> when the client opts in with
/// <c>Prefer: handling=strict</c>. Both a modifier on <c>semantic-text</c> and a chain terminating in it
/// have the same "silently widens instead of narrows" failure mode as an unsupported modifier on any
/// other parameter -- dropping the filter does not shrink the result set, it removes it entirely -- so
/// both must route through <see cref="SearchOptions.UnsupportedModifierParams"/> to get the same
/// reject-by-default treatment. These tests mirror the intent of
/// test/Ignixa.Api.E2ETests/Search/Modifiers/UnsupportedModifierHandlingTests.cs, but at the
/// SearchOptionsBuilder level, where the semantic parameter's own vector config can be wired directly.
/// </remarks>
public class SemanticSearchModifierHandlingTests
{
    private static readonly VectorSearchConfig ValidVectorConfig = new(
        VectorTextExtractionPolicy.Concatenate,
        MaxInputTokens: 8000,
        MinimumScore: 0m,
        ChunkSizeTokens: null,
        ChunkOverlapTokens: null);

    [Fact]
    public void GivenSemanticParam_WhenModifierSupplied_ThenUnsupportedModifierParamsContainsIt()
    {
        var harness = SearchOptionsBuilderHarness.ForPatientWithSemantic("semantic-text", ValidVectorConfig);

        var options = harness.Build([("semantic-text:exact", "chest pain")]);

        options.UnsupportedModifierParams.ShouldContain("semantic-text:exact");
    }

    [Fact]
    public void GivenSemanticParam_WhenChained_ThenUnsupportedModifierParamsContainsIt()
    {
        var harness = SearchOptionsBuilderHarness.ForObservationChainedToSemantic(
            "subject", "Patient", "semantic-text", ValidVectorConfig);

        var options = harness.Build([("subject:Patient.semantic-text", "chest pain")]);

        options.UnsupportedModifierParams.ShouldContain("subject:Patient.semantic-text");
    }
}
