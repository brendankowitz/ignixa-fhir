// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Indexing;

namespace Ignixa.Search.InMemory;

/// <summary>
/// Delegate that filters indexed resources based on search criteria.
/// Ported from microsoft/fhir-server feature/subscription-engine branch.
/// </summary>
/// <param name="input">Collection of resource keys with their search index entries</param>
/// <returns>Filtered collection of resource keys with search indices</returns>
[CLSCompliant(false)]
public delegate IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> SearchPredicate(
    IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> input);
