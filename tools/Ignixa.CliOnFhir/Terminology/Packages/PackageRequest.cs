// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.PackageManagement.Models;

namespace Ignixa.CliOnFhir.Terminology.Packages;

/// <summary>
/// A FHIR npm package asked for: <c>id@version</c>, <c>id#version</c> (the FHIR package cache spelling) or a bare
/// <c>id</c>, whose latest version is then resolved from the registry. Ids and versions follow the npm rules
/// <see cref="NpmPackageIdentity"/> enforces (lowercase name, exact SemVer 2 version); scoped names are not FHIR package ids.
/// </summary>
public sealed record PackageRequest
{
    public const string InvalidCode = "invalid-package";

    private PackageRequest(string id, string? version) => (Id, Version) = (id, version);

    public string Id { get; }

    /// <summary>The exact version; null for "the latest".</summary>
    public string? Version { get; }

    /// <exception cref="ToolRequestException">Not a package id, or a version that is not exact SemVer.</exception>
    public static PackageRequest Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string trimmed = text.Trim();
        int separator = trimmed.IndexOfAny(['@', '#']);
        string id = separator < 0 ? trimmed : trimmed[..separator];
        string? version = separator < 0 ? null : trimmed[(separator + 1)..];
        try
        {
            // The identity record checks the name and (when given) the version; a placeholder version checks a bare name.
            var identity = new NpmPackageIdentity(id, version ?? "0.0.0");
            if (id.Length == 0 || id.StartsWith('@'))
            {
                throw new ArgumentException("Scoped names are not FHIR package ids.", nameof(text));
            }

            return new PackageRequest(identity.Name, version is null ? null : identity.Version);
        }
        catch (ArgumentException)
        {
            throw ToolRequestException.Error(
                InvalidCode,
                "A package is 'id@version' (for example hl7.fhir.r4.core@4.0.1) or a bare id: a lowercase npm name and an exact SemVer version, not a range or a tag.");
        }
    }

    public static PackageRequest Exact(string id, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        return Parse($"{id}@{version}");
    }

    public override string ToString() => Version is null ? Id : $"{Id}@{Version}";
}
