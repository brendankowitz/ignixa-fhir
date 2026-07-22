// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Api.E2ETests._Infrastructure;

/// <summary>
/// A dedicated <see cref="IgnixaApiFixture"/> for the conformance run, pinned to its own
/// database. The conformance corpus creates hundreds of resources (e.g. all-resource-types
/// POSTs one of every resource type and does not delete them), which would leak into the
/// shared E2E database and break count-sensitive search tests. Its own database — and its
/// own in-process server, hence its own in-memory search index — keeps that data out of
/// every other E2E test.
/// </summary>
public sealed class ConformanceApiFixture : IgnixaApiFixture
{
    public ConformanceApiFixture() : base("FhirConformanceTest")
    {
    }
}
