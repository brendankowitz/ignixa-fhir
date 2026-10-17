// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Caching;

/// <summary>A persisted <see cref="Indexing.CompartmentIndex"/>, bound to the manifest it was written under.</summary>
/// <param name="ManifestId">Identity of the manifest the file belongs to; a different manifest makes it a plain miss.</param>
/// <param name="CompartmentType">The compartment type.</param>
/// <param name="BuiltAt">When the newest index it was read from was built.</param>
/// <param name="IndexedTypes">The types whose indexes it was read from.</param>
/// <param name="UnevaluableTypes">Types left out because their membership cannot be evaluated.</param>
/// <param name="Diagnostics">The diagnostics of those indexes.</param>
/// <param name="Compartments">Every compartment, each with its members grouped by type in order.</param>
internal sealed record CachedCompartmentIndex(
    string ManifestId,
    string CompartmentType,
    DateTimeOffset BuiltAt,
    IReadOnlyList<string> IndexedTypes,
    IReadOnlyList<string> UnevaluableTypes,
    IReadOnlyList<CatalogDiagnostic> Diagnostics,
    IReadOnlyList<CachedCompartment> Compartments)
{
    /// <summary>Checks that every member is present; JSON can hold a null for any of them.</summary>
    /// <exception cref="InvalidDataException">A required member is missing, or a compartment appears twice.</exception>
    public void Validate()
    {
        if (ManifestId is null || CompartmentType is null || IndexedTypes is null || UnevaluableTypes is null || Diagnostics is null || Compartments is null
            || IndexedTypes.Any(type => type is null) || UnevaluableTypes.Any(type => type is null)
            || Diagnostics.Any(diagnostic => diagnostic is null || diagnostic.Code is null || diagnostic.FilePath is null || diagnostic.Message is null))
        {
            throw new InvalidDataException("a required member of the compartment file is missing.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (CachedCompartment compartment in Compartments)
        {
            if (compartment?.Id is null || compartment.Types is null || compartment.Types.Any(group => group?.Type is null || group.Ids is null || group.Ids.Any(id => id is null)))
            {
                throw new InvalidDataException("a compartment in the file is incomplete.");
            }

            if (!ids.Add(compartment.Id))
            {
                throw new InvalidDataException($"the {CompartmentType} compartment of '{compartment.Id}' appears twice.");
            }
        }
    }
}
