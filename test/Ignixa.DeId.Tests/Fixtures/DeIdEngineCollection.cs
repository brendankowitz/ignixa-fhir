// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.DeId.Tests.Fixtures;

[CollectionDefinition("DeId Engine Collection")]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix - xUnit requires 'Collection' suffix for collection fixtures
public class DeIdEngineCollection : ICollectionFixture<DeIdEngineFixture>
#pragma warning restore CA1711
{
}
