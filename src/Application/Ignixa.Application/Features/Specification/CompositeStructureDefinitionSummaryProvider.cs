// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using IType = Ignixa.Abstractions.IType;
using PackageResourceProvider = Ignixa.PackageManagement.Infrastructure.PackageResourceProvider;

namespace Ignixa.Application.Features.Specification;

/// <summary>
/// Combines the core schema with installed definitions indexed by full canonical identity.
/// Only unambiguous, concrete custom specializations receive resource-type aliases.
/// </summary>
public class CompositeStructureDefinitionSummaryProvider : IFhirSchemaProvider
{
    private readonly IFhirSchemaProvider _baseProvider;
    private readonly IPackageResourceRepository _packageRepository;
    private readonly IPackageResourceProvider _packageResourceProvider;
    private readonly string? _fhirVersion;
    private readonly ILogger<CompositeStructureDefinitionSummaryProvider> _logger;
    private Lazy<Task<PackageDefinitions>> _definitions;

    public CompositeStructureDefinitionSummaryProvider(
        IFhirSchemaProvider baseProvider,
        IPackageResourceRepository packageRepository,
        IPackageResourceProvider packageResourceProvider,
        string? fhirVersion,
        ILogger<CompositeStructureDefinitionSummaryProvider> logger)
    {
        _baseProvider = baseProvider;
        _packageRepository = packageRepository;
        _packageResourceProvider = packageResourceProvider;
        _fhirVersion = fhirVersion;
        _logger = logger;
        _definitions = CreateDefinitions();
    }

    /// <summary>
    /// Loads one complete generation of package metadata. Canceling a waiter does not cancel
    /// initialization shared by other requests. Repository failures remain observable, and a
    /// later call retries a failed generation.
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => GetDefinitionsAsync(Volatile.Read(ref _definitions), cancellationToken);

    public IType? GetTypeDefinition(string typeName)
    {
        var baseType = _baseProvider.GetTypeDefinition(typeName);
        if (!typeName.Contains(':', StringComparison.Ordinal) && baseType != null)
        {
            return baseType;
        }

        var definitions = GetDefinitions();
        if (definitions.Types.TryGetValue(typeName, out var type))
        {
            return type;
        }
        if (definitions.ResourceCanonicals.TryGetValue(typeName, out var canonical))
        {
            return definitions.Types[canonical];
        }
        if (definitions.Backbones.TryGetValue(typeName, out type))
        {
            return type;
        }

        type = PackageResourceProvider.ResolveSnapshotType(typeName, rootName =>
            definitions.Types.GetValueOrDefault(rootName)
            ?? (definitions.ResourceCanonicals.TryGetValue(rootName, out var rootCanonical)
                ? definitions.Types[rootCanonical] : null));
        if (type != null)
        {
            return definitions.Backbones.GetOrAdd(typeName, type);
        }
        return baseType;
    }

    /// <summary>Returns the defining canonical for a resource-type alias, without guessing a core URL.</summary>
    public string? GetResourceCanonical(string resourceType)
        => _baseProvider.ResourceTypeNames.Contains(resourceType)
            ? $"http://hl7.org/fhir/StructureDefinition/{resourceType}"
            : GetDefinitions().ResourceCanonicals.GetValueOrDefault(resourceType);

    public bool IsKnownType(string typeName) => GetTypeDefinition(typeName) != null;

    /// <summary>
    /// Atomically replaces the generation, including negative results and backbone caches.
    /// An older in-flight load cannot publish into the replacement generation.
    /// </summary>
    public void ClearCache()
    {
        Interlocked.Exchange(ref _definitions, CreateDefinitions());
        _logger.LogInformation("Cleared composite provider cache (FHIR version: {FhirVersion})", _fhirVersion);
    }

    public FhirVersion Version => _baseProvider.Version;
    public string FullVersion => _baseProvider.FullVersion;
    public IReadOnlySet<string> ResourceTypeNames => GetDefinitions().ResourceNames;
    public IReferenceMetadataProvider ReferenceMetadataProvider => _baseProvider.ReferenceMetadataProvider;
    public IValueSetProvider ValueSetProvider => _baseProvider.ValueSetProvider;

    // ISchema is synchronous. Start shared initialization on the thread pool so a caller's
    // synchronization context cannot deadlock a synchronous schema lookup.
    private Lazy<Task<PackageDefinitions>> CreateDefinitions()
        => new(() => Task.Run(() => LoadDefinitionsAsync(CancellationToken.None)));

    private PackageDefinitions GetDefinitions()
        => GetDefinitionsAsync(Volatile.Read(ref _definitions), CancellationToken.None).GetAwaiter().GetResult();

    private async Task<PackageDefinitions> GetDefinitionsAsync(
        Lazy<Task<PackageDefinitions>> generation,
        CancellationToken cancellationToken)
    {
        var load = generation.Value;
        try
        {
            return await load.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (load.IsFaulted || load.IsCanceled)
        {
            // Caller cancellation alone does not retire a healthy shared load. A late failure
            // from an older generation must not replace a newer ClearCache/retry generation.
            Interlocked.CompareExchange(ref _definitions, CreateDefinitions(), generation);
            throw;
        }
    }

    private async Task<PackageDefinitions> LoadDefinitionsAsync(CancellationToken cancellationToken)
    {
        var resources = await _packageRepository.GetAllStructureDefinitionsAsync(_fhirVersion, cancellationToken)
            .ConfigureAwait(false);
        var types = new Dictionary<string, IType>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);

        foreach (var resource in resources)
        {
            if (!resource.IsActive || resource.ResourceType != "StructureDefinition")
            {
                continue;
            }
            var type = _packageResourceProvider.ToTypeDefinition(resource.ResourceJson, resource.FhirVersion);
            if (type == null || string.IsNullOrEmpty(resource.Canonical)
                || type is ITypeExtended { CanonicalUrl: { } identity } && identity != resource.Canonical)
            {
                _logger.LogWarning("Cannot load package StructureDefinition {Canonical}", resource.Canonical);
                continue;
            }
            types.TryAdd(resource.Canonical, type);
            if (!string.IsNullOrEmpty(resource.Version))
            {
                types.TryAdd($"{resource.Canonical}|{resource.Version}", type);
            }

            if (!ReferenceEquals(types[resource.Canonical], type)
                || !IsCustomResourceDefinition(resource.ResourceJson, type) || ambiguous.Contains(type.Info.Name))
            {
                continue;
            }
            if (aliases.TryGetValue(type.Info.Name, out var existing) && existing != resource.Canonical)
            {
                aliases.Remove(type.Info.Name);
                ambiguous.Add(type.Info.Name);
                _logger.LogWarning("Ambiguous custom resource type {TypeName}: {FirstCanonical} and {SecondCanonical}",
                    type.Info.Name, existing, resource.Canonical);
                continue;
            }
            aliases.TryAdd(type.Info.Name, resource.Canonical);
        }

        var names = new HashSet<string>(_baseProvider.ResourceTypeNames, StringComparer.Ordinal);
        names.UnionWith(aliases.Keys);
        return new PackageDefinitions(types, aliases, names);
    }

    private bool IsCustomResourceDefinition(string json, IType type)
    {
        if (type.Info.IsAbstract || _baseProvider.IsKnownType(type.Info.Name))
        {
            return false;
        }
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return root.TryGetProperty("derivation", out var derivation) && derivation.ValueKind == JsonValueKind.String
            && derivation.GetString() == "specialization"
            && root.TryGetProperty("abstract", out var abstractElement) && abstractElement.ValueKind == JsonValueKind.False
            && root.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String
            && kind.GetString() is "logical" or "resource";
    }

    private sealed record PackageDefinitions(
        IReadOnlyDictionary<string, IType> Types,
        IReadOnlyDictionary<string, string> ResourceCanonicals,
        IReadOnlySet<string> ResourceNames)
    {
        public ConcurrentDictionary<string, IType> Backbones { get; } = new(StringComparer.Ordinal);
    }
}
