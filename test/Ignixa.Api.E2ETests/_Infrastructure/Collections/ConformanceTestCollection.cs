// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.E2ETests._Infrastructure;

namespace Ignixa.Api.E2ETests._Infrastructure.Collections;

/// <summary>
/// xUnit collection for the conformance run, separate from <see cref="E2ETestCollection"/>
/// so it uses its own <see cref="ConformanceApiFixture"/> (own server, own database). The
/// conformance corpus creates large amounts of data that must not reach the count-sensitive
/// search and CRUD tests in the shared collection.
/// </summary>
// CA1711 suppressed: xUnit requires collection definitions to end with "Collection".
#pragma warning disable CA1711
[CollectionDefinition(Name)]
public class ConformanceTestCollection : ICollectionFixture<ConformanceApiFixture>
#pragma warning restore CA1711
{
    public const string Name = "Conformance Tests";
}
