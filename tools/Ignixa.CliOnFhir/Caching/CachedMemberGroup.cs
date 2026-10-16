// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Caching;

/// <summary>The members of one type in a persisted compartment, in catalog order.</summary>
internal sealed record CachedMemberGroup(string Type, IReadOnlyList<string> Ids);
