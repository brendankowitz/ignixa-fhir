// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.E2ETests._Infrastructure;

namespace Ignixa.Api.E2ETests._Infrastructure.Collections;

/// <summary>
/// xUnit collection for semantic (vector) search E2E coverage, separate from
/// <see cref="E2ETestCollection"/> so it uses its own <see cref="SemanticSearchApiFixture"/> (own
/// server, own database, VectorSearch enabled). Keeps that feature flag and the custom
/// <c>semantic-text</c> SearchParameter it activates out of the shared fixture every other E2E test
/// depends on.
/// </summary>
// CA1711 suppressed: xUnit requires collection definitions to end with "Collection".
#pragma warning disable CA1711
[CollectionDefinition(Name)]
public class SemanticSearchTestCollection : ICollectionFixture<SemanticSearchApiFixture>
#pragma warning restore CA1711
{
    public const string Name = "Semantic Search Tests";
}
