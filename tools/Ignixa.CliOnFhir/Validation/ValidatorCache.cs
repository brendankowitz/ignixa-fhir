// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Npm;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// A session's validators: one <see cref="ProfileSet"/> per distinct list of requested packages, built on first use and kept
/// for the session (its resolver caches the schemas it builds, which is most of a validation's fixed cost), and the terminology
/// adapter every binding check asks. Safe to use concurrently.
/// </summary>
internal sealed class ValidatorCache(FolderSession session)
{
    private readonly ConcurrentDictionary<string, Lazy<ProfileSet>> _sets = new(StringComparer.Ordinal);

    /// <summary>The terminology adapter over the session's store.</summary>
    public StoreTerminologyService Terminology { get; } = new(session);

    /// <summary>The profile set over the folder's StructureDefinitions and those of <paramref name="packages"/>, in that order.</summary>
    public ProfileSet For(IReadOnlyList<AcquiredPackage> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        string key = string.Join(' ', packages.Select(package => $"{package.Id}@{package.Version}"));
        return _sets.GetOrAdd(key, _ => new Lazy<ProfileSet>(
            () =>
            {
                List<CatalogEntry> folderDefinitions = [.. session.Catalog.Entries.Where(entry => entry.ResourceType == "StructureDefinition")];
                return ProfileSet.Build(session.Schema, session.RootPath, folderDefinitions, packages, Terminology);
            },
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }
}
