// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.GraphQl.Contracts;
using Ignixa.Application.Features.Search;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.GraphQl.Schema;

/// <summary>
/// Owns the GraphQL type module for each FHIR version, creating one only when its schema is built.
/// </summary>
/// <remarks>
/// Creating a module loads that version's base schema provider and search parameter definitions, so a
/// server that serves only R4 must never create the others. <see cref="Created"/> exposes only modules
/// that exist: a module never created has no schema to invalidate, and creating it just to notify it
/// would load every FHIR version the server does not serve.
/// </remarks>
public sealed class FhirTypeModuleCatalog(
    IFhirVersionContext versionContext,
    IFhirBaseUriProvider baseUriProvider,
    ILoggerFactory loggerFactory)
{
    // PublicationOnly so a failed creation is retried on the next schema build rather than cached.
    private readonly ConcurrentDictionary<FhirVersion, Lazy<FhirTypeModule>> _modules = new();

    public FhirTypeModule GetOrCreate(FhirVersion version) =>
        _modules.GetOrAdd(
                version,
                v => new Lazy<FhirTypeModule>(() => Create(v), LazyThreadSafetyMode.PublicationOnly))
            .Value;

    public IEnumerable<IFhirTypeModule> Created =>
        _modules.Values.Where(module => module.IsValueCreated).Select(module => module.Value);

    private FhirTypeModule Create(FhirVersion version) =>
        new(
            versionContext.GetBaseSchemaProvider(version),
            versionContext.GetSearchParameterDefinitionManager(version),
            baseUriProvider,
            loggerFactory.CreateLogger<FhirTypeModule>());
}
