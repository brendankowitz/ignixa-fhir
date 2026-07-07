// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure.Snapshot;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// <see cref="IFhirSchemaProvider"/> that delegates to a base FHIR-version schema provider
/// and additionally exposes profile <c>StructureDefinition</c>s extracted from one or more
/// loaded IG packages. Profiles are indexed by their resource <c>id</c> - the last URL
/// segment that <c>StructureDefinitionSchemaResolver</c> uses to look up via
/// <see cref="ISchema.GetTypeDefinition(string)"/>.
/// <para>
/// When multiple packages declare a profile with the same id, the last one added wins.
/// Use ordering to express precedence (e.g. layer IG-specific profiles after their
/// base IG so the IG wins).
/// </para>
/// </summary>
public sealed class ProfileLayeredSchemaProvider : IFhirSchemaProvider
{
    private readonly IFhirSchemaProvider _base;
    private readonly Dictionary<string, IType> _profileTypes;

    /// <summary>
    /// Initializes a new instance with the given base provider and a collection of
    /// extracted package resources whose <c>StructureDefinition</c> entries are added
    /// to the profile index.
    /// </summary>
    /// <param name="baseProvider">Base FHIR schema provider (R4/R4B/R5/STU3 core).</param>
    /// <param name="packageResources">Conformance resources extracted from one or more IG packages.</param>
    /// <param name="logger">
    /// Optional logger. When a package <c>StructureDefinition</c> cannot be adapted (malformed
    /// JSON, differential-only definition with no snapshot, or missing id), the profile is dropped
    /// from the index and a warning is logged so the silent downgrade to base-only validation is
    /// observable. Defaults to <see cref="NullLogger{T}"/>.
    /// </param>
    public ProfileLayeredSchemaProvider(
        IFhirSchemaProvider baseProvider,
        IEnumerable<ExtractedResource> packageResources,
        ILogger<ProfileLayeredSchemaProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(baseProvider);
        ArgumentNullException.ThrowIfNull(packageResources);

        _base = baseProvider;
        _profileTypes = new Dictionary<string, IType>(StringComparer.Ordinal);
        var log = logger ?? NullLogger<ProfileLayeredSchemaProvider>.Instance;

        var provider = new PackageResourceProvider(
            new LoggerAdapter<PackageResourceProvider>(log));

        // Materialize once: the collection is enumerated to build the base resolver index and
        // again in the adaptation loop, so a lazy source would be walked twice.
        var resources = packageResources as IReadOnlyList<ExtractedResource> ?? packageResources.ToList();
        var snapshotGenerator = new SnapshotGenerator();
        var baseResolver = new PackageSnapshotBaseResolver(resources, baseProvider);

        foreach (var res in resources)
        {
            if (res.ResourceType != "StructureDefinition")
            {
                continue;
            }
            var resourceJson = BackfillSnapshotIfNeeded(res, snapshotGenerator, baseResolver, log);
            var type = provider.ToTypeDefinition(resourceJson, baseProvider.FullVersion);
            if (type != null && !string.IsNullOrEmpty(res.ResourceId))
            {
                if (_profileTypes.ContainsKey(res.ResourceId))
                {
                    log.LogWarning(
                        "Profile id '{ProfileId}' (canonical='{Canonical}') overwrites an existing profile entry — last-wins. Check package ordering if this is unintended.",
                        res.ResourceId,
                        res.Canonical);
                }
                else if (_base.IsKnownType(res.ResourceId))
                {
                    log.LogWarning(
                        "Profile id '{ProfileId}' (canonical='{Canonical}') shadows a base-spec type — profile takes precedence. Validate against the base type will use the profile definition.",
                        res.ResourceId,
                        res.Canonical);
                }
                _profileTypes[res.ResourceId] = type;
            }
            else
            {
                log.LogWarning(
                    "Profile StructureDefinition (id='{ProfileId}', canonical='{Canonical}') could not be adapted and will not be available for profile validation. Resources declaring this profile validate against the base resource definition only.",
                    res.ResourceId,
                    res.Canonical);
            }
        }
    }

    /// <summary>
    /// Returns the resource JSON to hand to the adapter, generating a <c>snapshot</c> first when
    /// the profile ships only a <c>differential</c> + <c>baseDefinition</c>. Profiles that already
    /// carry a snapshot are passed through unchanged (parity with the reference generator: no
    /// regeneration). A circular base chain is logged and the original JSON is returned, letting
    /// the adapter drop the profile exactly as it did before snapshot generation existed.
    /// </summary>
    private static string BackfillSnapshotIfNeeded(
        ExtractedResource res,
        SnapshotGenerator generator,
        ISnapshotBaseResolver resolver,
        ILogger log)
    {
        JsonObject? structureDefinition;
        try
        {
            structureDefinition = JsonNode.Parse(res.ResourceJson) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed JSON — leave it to the adapter, which logs and drops it as before.
            return res.ResourceJson;
        }

        if (structureDefinition is null)
        {
            return res.ResourceJson;
        }

        if (structureDefinition["snapshot"] is JsonObject existing
            && existing["element"] is JsonArray { Count: > 0 })
        {
            return res.ResourceJson;
        }

        try
        {
            var generated = generator.GenerateSnapshotElements(structureDefinition, resolver);
            if (generated is null)
            {
                return res.ResourceJson;
            }

            structureDefinition["snapshot"] = new JsonObject { ["element"] = generated };
            return structureDefinition.ToJsonString();
        }
        catch (SnapshotGenerationException ex)
        {
            log.LogWarning(
                ex,
                "Snapshot generation failed for profile (id='{ProfileId}', canonical='{Canonical}'); it validates against the base resource definition only.",
                res.ResourceId,
                res.Canonical);
            return res.ResourceJson;
        }
    }

    /// <inheritdoc/>
    public FhirVersion Version => _base.Version;

    /// <inheritdoc/>
    public string FullVersion => _base.FullVersion;

    /// <inheritdoc/>
    public IReadOnlySet<string> ResourceTypeNames => _base.ResourceTypeNames;

    /// <inheritdoc/>
    public IReferenceMetadataProvider ReferenceMetadataProvider => _base.ReferenceMetadataProvider;

    /// <inheritdoc/>
    public IValueSetProvider ValueSetProvider => _base.ValueSetProvider;

    /// <inheritdoc/>
    public IType? GetTypeDefinition(string typeName)
        => _profileTypes.TryGetValue(typeName, out var profile) ? profile : _base.GetTypeDefinition(typeName);

    /// <inheritdoc/>
    public bool IsKnownType(string typeName)
        => _profileTypes.ContainsKey(typeName) || _base.IsKnownType(typeName);

    /// <summary>
    /// Bridges a <see cref="ILogger{TCategoryName}"/> of one category to another typed logger,
    /// forwarding all log calls with the same level, message, and exception.
    /// Used to pass the outer logger into <see cref="PackageResourceProvider"/> without
    /// requiring an <c>ILoggerFactory</c> on the constructor.
    /// </summary>
    private sealed class LoggerAdapter<T>(ILogger source) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => source.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel)
            => source.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => source.Log(logLevel, eventId, state, exception, formatter);
    }
}
