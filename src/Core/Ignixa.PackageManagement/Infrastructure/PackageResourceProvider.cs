// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Converts package resource JSON to IType for use in composite schema provider.
/// Parses FHIR StructureDefinition JSON using internal infrastructure (no Firely SDK).
/// </summary>
public class PackageResourceProvider : IPackageResourceProvider
{
    private readonly ILogger<PackageResourceProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackageResourceProvider"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public PackageResourceProvider(ILogger<PackageResourceProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Converts a package resource JSON to an IType using
    /// <see cref="StructureDefinitionTypeAdapter"/>.
    /// </summary>
    /// <param name="resourceJson">The FHIR StructureDefinition resource as JSON string.</param>
    /// <param name="fhirVersion">The FHIR version (e.g., "4.0.1", "4.3.0", "5.0.0").</param>
    /// <returns>The type definition if parsing succeeds, null otherwise.</returns>
    public IType? ToTypeDefinition(string resourceJson, string fhirVersion)
    {
        if (string.IsNullOrEmpty(resourceJson) || string.IsNullOrEmpty(fhirVersion))
        {
            return null;
        }

        try
        {
            return new StructureDefinitionTypeAdapter().Adapt(resourceJson, fhirVersion);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            _logger.LogWarning(ex, "Failed to adapt StructureDefinition (fhirVersion={FhirVersion}) - returning null", fhirVersion);
            return null;
        }
    }

    /// <summary>
    /// Resolves a qualified inline snapshot type or canonical contentReference without
    /// replacing its canonical identity with a URL-tail alias.
    /// </summary>
    public static IType? ResolveSnapshotType(string typeName, Func<string, IType?> resolveRoot)
        => ResolveSnapshotType(typeName, resolveRoot, new HashSet<string>(StringComparer.Ordinal));

    private static IType? ResolveSnapshotType(
        string typeName, Func<string, IType?> resolveRoot, HashSet<string> visiting)
    {
        if (!visiting.Add(typeName))
        {
            return null;
        }
        try
        {
            int fragment = typeName.IndexOf('#', StringComparison.Ordinal);
            int dot = typeName.IndexOf('.', StringComparison.Ordinal);
            if (fragment < 0 && (dot < 0 || typeName.Contains(':', StringComparison.Ordinal)))
            {
                return null;
            }
            string rootKey = fragment >= 0 ? typeName[..fragment] : typeName[..dot];
            var root = resolveRoot(rootKey);
            if (root == null)
            {
                return null;
            }
            string path = fragment >= 0 ? typeName[(fragment + 1)..] : root.Info.Name + typeName[dot..];
            if (!path.StartsWith(root.Info.Name + ".", StringComparison.Ordinal))
            {
                return null;
            }

            IType current = root;
            string qualifiedName = fragment >= 0 ? rootKey + "#" + root.Info.Name : rootKey;
            foreach (string segment in path[(root.Info.Name.Length + 1)..].Split('.'))
            {
                var child = current.Children.FirstOrDefault(c => c.Info.Name == segment);
                if (child == null)
                {
                    return null;
                }
                qualifiedName += "." + segment;
                if (child is ITypeExtended { ContentReference: { Length: > 0 } reference })
                {
                    string target = reference.StartsWith('#') ? rootKey + reference : reference;
                    int targetFragment = target.IndexOf('#', StringComparison.Ordinal);
                    if (targetFragment > 0 && rootKey.Contains(':', StringComparison.Ordinal)
                        && target[..targetFragment] == rootKey.Split('|')[0])
                    {
                        target = rootKey + target[targetFragment..];
                    }
                    child = ResolveSnapshotType(target, resolveRoot, visiting);
                    if (child == null)
                    {
                        return null;
                    }
                    qualifiedName = child.Info.Name;
                    int ownerFragment = qualifiedName.IndexOf('#', StringComparison.Ordinal);
                    if (ownerFragment >= 0)
                    {
                        rootKey = qualifiedName[..ownerFragment];
                    }
                }
                current = child;
            }

            if (current is not ITypeExtended metadata || current.Children.Count == 0)
            {
                return null;
            }
            return new AdaptedType(
                new TypeInfo(qualifiedName, current.Info.Primitive, isModifier: current.Info.IsModifier),
                current.IsCollection, current.IsRequired, current.Order, metadata.Min, metadata.Max,
                current.Children, metadata.Constraints, metadata.Binding, metadata.FixedValue,
                metadata.PatternValue, metadata.Types, metadata.DefaultTypeName, metadata.ReferenceTargets,
                metadata.ContentReference, metadata.Slicing, current.InSummary, metadata.CanonicalUrl);
        }
        finally
        {
            visiting.Remove(typeName);
        }
    }
}
