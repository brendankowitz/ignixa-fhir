// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using EnsureThat;
using Ignixa.Abstractions;
using Microsoft.Extensions.Logging;
using Ignixa.Specification;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Indexing;

public static class SearchIndexerFactory
{
    /// <param name="baseUriProvider">
    /// Supplies this server's base URIs so an absolute self-reference is indexed in the same form as the
    /// equivalent relative one. Must be the same provider the query path uses; if the two disagree, a
    /// reference stored under one form will not be found by a search issued in the other. Required for that
    /// reason — pass <see cref="NullFhirBaseUriProvider.Instance"/> to opt out deliberately.
    /// </param>
    public static ISearchIndexer CreateInstance(
        IFhirSchemaProvider fhirSchemaProvider,
        ILoggerFactory loggerProvider,
        ISearchParameterDefinitionManager searchParameterDefinitionManager,
        IFhirBaseUriProvider baseUriProvider)
    {
        ArgumentNullException.ThrowIfNull(baseUriProvider);

        // If no manager provided, create new instance (backward compatibility)
        var definitionManager = searchParameterDefinitionManager
            ?? new SearchParameterDefinitionManager(fhirSchemaProvider, loggerProvider.CreateLogger<SearchParameterDefinitionManager>());

        var (converterManager, elementResolver) = CreateIndexingComponents(fhirSchemaProvider, baseUriProvider);

        return new ElementSearchIndexer(
            new SupportedSearchParameterDefinitionManager(definitionManager),
            converterManager,
            elementResolver,
            loggerProvider.CreateLogger<ElementSearchIndexer>());
    }

    internal static (
        IElementToSearchValueConverterManager ConverterManager,
        IReferenceToElementResolver ElementResolver) CreateIndexingComponents(
            IFhirSchemaProvider fhirSchemaProvider,
            IFhirBaseUriProvider baseUriProvider)
    {
        var referenceParser = new ReferenceSearchValueParser(fhirSchemaProvider, baseUriProvider);
        var elementResolver = new LightweightReferenceToElementResolver(referenceParser, fhirSchemaProvider);

        return (
            new FhirElementToSearchValueConverterManager(
                CreateConverters(fhirSchemaProvider, referenceParser, elementResolver)),
            elementResolver);
    }

    /// <summary>
    /// Discovers and constructs every shipped converter, which is the set
    /// <see cref="FhirElementToSearchValueConverterManager"/> keys by (FHIR type, search value type).
    /// </summary>
    /// <remarks>
    /// Exposed separately from <see cref="CreateIndexingComponents"/> so the registration census can
    /// enumerate what production actually registers rather than restating it. The manager answers
    /// "is this pair covered?" but not "what is covered?", and a census that had to hardcode the second
    /// question would pass on a table rather than on the code.
    /// </remarks>
    internal static IReadOnlyList<IElementToSearchValueConverter> CreateConverters(
        IFhirSchemaProvider fhirSchemaProvider,
        ReferenceSearchValueParser referenceParser,
        IReferenceToElementResolver elementResolver)
    {
        var codesystems = new CodeSystemResolver(fhirSchemaProvider.Version);

        return typeof(ElementSearchIndexer)
            .Assembly
            .ExportedTypes
            .Where(x => typeof(IElementToSearchValueConverter).IsAssignableFrom(x) && !x.IsAbstract && !x.IsGenericType)
            .Select(x => (IElementToSearchValueConverter)CreateTypeWithArguments(x, fhirSchemaProvider, referenceParser, elementResolver, codesystems, fhirSchemaProvider.Version))
            .ToArray();
    }

    private static object CreateTypeWithArguments(Type type, params object[] argOverrides)
    {
        EnsureArg.IsNotNull(type, nameof(type));

        if (argOverrides.Any(x => x == null)) throw new ArgumentNullException(nameof(argOverrides), "Values for argument overrides should not be null");

        ConstructorInfo constructor = type.GetConstructors().OrderBy(x => x.GetParameters().Length).FirstOrDefault();

        if (constructor == null) throw new ArgumentException($"{type} has no usable constructors", nameof(type));

        var arguments = new List<object>();
        foreach (ParameterInfo parameter in constructor.GetParameters())
        {
            object overridden = argOverrides.FirstOrDefault(x => parameter.ParameterType.IsAssignableFrom(x.GetType()));
            if (overridden != null)
            {
                arguments.Add(overridden);
            }
            else
            {
                if (parameter.ParameterType.IsClass && !parameter.ParameterType.GetConstructors().Any()) throw new ArgumentException($"{parameter.ParameterType} has no usable constructors. Used to create {type}", nameof(type));

                if (parameter.ParameterType.IsClass && parameter.ParameterType.GetConstructors().Min(x => x.GetParameters().Length) > 0)
                    arguments.Add(CreateTypeWithArguments(parameter.ParameterType, argOverrides));
                else
                    throw new ArgumentNullException(nameof(argOverrides), $"Unable to find a value for {parameter.ParameterType}");
            }
        }

        return Activator.CreateInstance(type, arguments.ToArray());
    }
}
