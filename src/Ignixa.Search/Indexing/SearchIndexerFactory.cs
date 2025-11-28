// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Ignixa.Specification;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Indexing;

public static class SearchIndexerFactory
{
    public static ISearchIndexer CreateInstance(
        IFhirSchemaProvider fhirSchemaProvider,
        ILoggerFactory loggerProvider,
        ISearchParameterDefinitionManager searchParameterDefinitionManager = null)
    {
        // If no manager provided, create new instance (backward compatibility)
        var definitionManager = searchParameterDefinitionManager
            ?? new SearchParameterDefinitionManager(fhirSchemaProvider, loggerProvider.CreateLogger<SearchParameterDefinitionManager>());

        var referenceParser = new ReferenceSearchValueParser(fhirSchemaProvider);
        var elementResolver = new LightweightReferenceToElementResolver(referenceParser, fhirSchemaProvider);
        var codesystems = new CodeSystemResolver(fhirSchemaProvider.Version);

        IElementToSearchValueConverter[] converters = typeof(ElementSearchIndexer)
            .Assembly
            .ExportedTypes
            .Where(x => typeof(IElementToSearchValueConverter).IsAssignableFrom(x) && !x.IsAbstract && !x.IsGenericType)
            .Select(x => (IElementToSearchValueConverter)CreateTypeWithArguments(x, fhirSchemaProvider, referenceParser, elementResolver, codesystems, fhirSchemaProvider.Version))
            .ToArray();

        // Manager is now initialized synchronously in constructor with pre-generated search parameters

        return new ElementSearchIndexer(
            new SupportedSearchParameterDefinitionManager(definitionManager),
            new FhirElementToSearchValueConverterManager(converters),
            elementResolver,
            loggerProvider.CreateLogger<ElementSearchIndexer>());
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
