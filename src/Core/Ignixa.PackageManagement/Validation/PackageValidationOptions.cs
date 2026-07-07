// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Validation;

/// <summary>
/// Inputs for constructing a package-backed validation setup via <see cref="PackageBackedValidator"/>.
/// Describes a base FHIR-version schema plus the conformance resources of one or more loaded
/// packages (profiles, extensions, ValueSets, CodeSystems) to layer over it.
/// </summary>
public sealed class PackageValidationOptions
{
    /// <summary>
    /// The base FHIR-version schema provider (e.g. <c>R4CoreSchemaProvider</c>) that the package
    /// conformance resources are layered over. Supplied by the caller so this assembly stays
    /// FHIR-version agnostic.
    /// </summary>
    public required IFhirSchemaProvider BaseSchemaProvider { get; init; }

    /// <summary>
    /// Conformance resources extracted from the loaded packages: <c>StructureDefinition</c> profiles
    /// and extensions are layered into the schema; <c>ValueSet</c>/<c>CodeSystem</c> resources feed
    /// the terminology surface.
    /// </summary>
    public IReadOnlyList<ExtractedResource> PackageResources { get; init; } = [];

    /// <summary>
    /// When true, package <c>StructureDefinition</c>s whose id collides with a base-spec type name
    /// (e.g. <c>Patient</c>, <c>string</c>) are not layered into the schema, so a package can be
    /// loaded for its extensions/profiles and CodeSystems <b>without</b> shadowing the generated
    /// base resource/datatype definitions. Use this when loading a core package purely to make
    /// extensions and CodeSystems resolve while keeping base validation byte-identical.
    /// Defaults to false (all profiles layered — the IG-scenario behaviour).
    /// </summary>
    public bool ExcludeBaseTypeStructureDefinitions { get; init; }

    /// <summary>
    /// When true, package <c>ValueSet</c>s are layered into the terminology service (queried before
    /// the base provider) so IG-defined value sets participate in binding validation. When false,
    /// only <c>CodeSystem</c> content is exposed (for code&#8594;display resolution) and the binding
    /// path uses the base provider unchanged. Defaults to true (the IG-scenario behaviour).
    /// </summary>
    public bool LayerPackageValueSets { get; init; } = true;

    /// <summary>
    /// Optional logger factory used to observe package-adaptation warnings (dropped profiles,
    /// unexpandable value sets, ...). Defaults to no logging.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; init; }
}
