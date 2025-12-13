// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.E2ETests._Infrastructure;

namespace Ignixa.Api.E2ETests._Infrastructure.Collections;

/// <summary>
/// xUnit collection definition that ensures all E2E tests share a single
/// IgnixaApiFixture instance. This prevents database schema race conditions
/// when multiple test classes try to initialize the same SQL Server database.
/// </summary>
/// <remarks>
/// All E2E test classes should use [Collection(E2ETestCollection.Name)]
/// instead of implementing IClassFixture directly. This ensures:
/// - Database is initialized only once per test run
/// - Tests within the collection run sequentially (no parallel conflicts)
/// - Single WebApplicationFactory instance is reused
/// </remarks>
// CA1711 suppressed: xUnit convention requires collection definitions to end with "Collection"
// for ICollectionFixture<T> pattern to work correctly. This is a standard xUnit pattern.
#pragma warning disable CA1711
[CollectionDefinition(Name)]
public class E2ETestCollection : ICollectionFixture<IgnixaApiFixture>
#pragma warning restore CA1711
{
    /// <summary>
    /// Collection name constant used by test classes.
    /// </summary>
    public const string Name = "E2E Tests";
}
