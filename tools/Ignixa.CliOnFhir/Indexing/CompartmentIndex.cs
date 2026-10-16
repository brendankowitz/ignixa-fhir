// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>
/// Every compartment of one type in the folder (every Patient's, say) with its members, worked out once from the search
/// indexes of the types that can be in it and persisted with them, so one patient's context needs neither those indexes nor
/// a pass over them.
/// </summary>
/// <param name="CompartmentType">The compartment type, for example <c>Patient</c>.</param>
/// <param name="Members">
/// Each compartment's members by the id of its focal resource, type by type in ordinal order and in catalog order within a
/// type, as <c>FolderSearchSource.CompartmentMembers</c> returns them. Every focal resource the folder holds has an entry.
/// </param>
/// <param name="IndexedTypes">The types whose indexes membership was read from, the focal type first.</param>
/// <param name="UnevaluableTypes">Types the definition makes impossible to evaluate, so they are in no compartment.</param>
/// <param name="Diagnostics">The diagnostics of the indexes read, as built.</param>
/// <param name="BuiltAt">When the newest of the indexes read was built.</param>
/// <param name="FromCache">Whether it was loaded from the persisted cache rather than worked out in this process.</param>
public sealed record CompartmentIndex(
    string CompartmentType,
    IReadOnlyDictionary<string, IReadOnlyList<CatalogEntry>> Members,
    IReadOnlyList<string> IndexedTypes,
    IReadOnlyList<string> UnevaluableTypes,
    IReadOnlyList<CatalogDiagnostic> Diagnostics,
    DateTimeOffset BuiltAt,
    bool FromCache)
{
    /// <summary>The members of the compartment of <paramref name="id"/>; empty when the folder holds no such focal resource.</summary>
    public IReadOnlyList<CatalogEntry> MembersOf(string id) => Members.TryGetValue(id, out IReadOnlyList<CatalogEntry>? members) ? members : [];
}
