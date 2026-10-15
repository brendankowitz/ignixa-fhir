// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Api.E2ETests._Infrastructure.Collections;

/// <summary>
/// xUnit collection for tests that need <see cref="ElementOrderingApiFixture"/>'s non-default
/// serialization setting, kept apart from the shared <see cref="E2ETestCollection"/> server.
/// </summary>
// CA1711 suppressed: repository convention names xUnit collection definitions with a "Collection" suffix.
#pragma warning disable CA1711
[CollectionDefinition(Name)]
public class ElementOrderingTestCollection : ICollectionFixture<ElementOrderingApiFixture>
#pragma warning restore CA1711
{
    public const string Name = "Element Ordering Tests";
}
