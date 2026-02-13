// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Anonymizer.Tests.Fixtures;

[CollectionDefinition("Anonymizer Engine Collection")]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix - xUnit requires 'Collection' suffix for collection fixtures
public class AnonymizerEngineCollection : ICollectionFixture<AnonymizerEngineFixture>
#pragma warning restore CA1711
{
}
