// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.Search.InMemory;

namespace Ignixa.CliOnFhir.Search;

/// <summary>Every resource a search matched, unpaged, with what produced them.</summary>
/// <param name="Parsed">The parsed query, the compartment applied.</param>
/// <param name="Source">The indexes the search read.</param>
/// <param name="Engine">The engine that evaluated it, for resolving includes.</param>
/// <param name="Matches">The matches, in <c>_sort</c> order or else catalog order.</param>
/// <param name="Issues">The parser's warnings and the compartment's notes; the caller adds to it.</param>
internal sealed record SearchMatch(ParsedSearch Parsed, FolderSearchSource Source, InMemorySearchEngine Engine, List<IndexedResource> Matches, List<IssueItem> Issues);
