// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Caching;

/// <summary>One indexed resource: its catalog id (the type is the file's) and its search index entries, in extraction order.</summary>
internal sealed record CachedResource(string Id, IReadOnlyList<CachedEntry> Entries);
