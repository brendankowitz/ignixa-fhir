// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Caching;

/// <summary>One persisted compartment: its focal resource's id and its members' ids, type by type in order.</summary>
internal sealed record CachedCompartment(string Id, IReadOnlyList<CachedMemberGroup> Types);
