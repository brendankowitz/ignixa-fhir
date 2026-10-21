// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Application.Tests.Search.Expressions.Parsers;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Search.Parsing;

/// <summary>Wires a real <see cref="SearchOptionsBuilder"/> over <see cref="SearchParserTestContext"/> for Patient-scoped outcome tests.</summary>
internal sealed class SearchOptionsBuilderHarness
{
    private readonly SearchOptionsBuilder _builder;
    private readonly string _resourceType;

    private SearchOptionsBuilderHarness(SearchOptionsBuilder builder, string resourceType = "Patient")
    {
        _builder = builder;
        _resourceType = resourceType;
    }

    public static SearchOptionsBuilderHarness ForPatient(params (string Code, SearchParamType Type)[] searchParameters)
    {
        var context = new SearchParserTestContext();
        foreach (var (code, type) in searchParameters)
        {
            context.Add("Patient", code, type);
        }

        return new SearchOptionsBuilderHarness(new SearchOptionsBuilder(context.Parser, context.DefinitionManager));
    }

    /// <summary>Wires a Patient search over a forward-chained reference, e.g. <c>general-practitioner.name</c>.</summary>
    public static SearchOptionsBuilderHarness ForPatientChainedThrough(
        string referenceCode,
        string targetResourceType,
        string targetCode,
        SearchParamType targetType)
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", referenceCode, SearchParamType.Reference, targets: [targetResourceType]);
        context.Add(targetResourceType, targetCode, targetType);

        return new SearchOptionsBuilderHarness(new SearchOptionsBuilder(context.Parser, context.DefinitionManager));
    }

    /// <summary>Wires a Patient search exposing a semantic (vector) search parameter.</summary>
    public static SearchOptionsBuilderHarness ForPatientWithSemantic(string code, VectorSearchConfig vectorConfig)
    {
        var resourceType = "Patient";

        var context = new SearchParserTestContext();
        var parameter = new SearchParameterInfo(
            name: code,
            code: code,
            searchParamType: SearchParamType.Special,
            url: new Uri($"http://ignixa.test/SearchParameter/{resourceType}-{code}"),
            baseResourceTypes: new[] { resourceType },
            vectorConfig: vectorConfig);

        context.AddCommon(parameter, resourceType);

        return new SearchOptionsBuilderHarness(new SearchOptionsBuilder(context.Parser, context.DefinitionManager));
    }

    /// <summary>
    /// Wires an Observation search over a forward-chained reference terminating in a semantic parameter,
    /// e.g. <c>subject:Patient.semantic-text</c>.
    /// </summary>
    public static SearchOptionsBuilderHarness ForObservationChainedToSemantic(
        string referenceCode,
        string targetResourceType,
        string semanticCode,
        VectorSearchConfig vectorConfig)
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", referenceCode, SearchParamType.Reference, targets: [targetResourceType]);

        var parameter = new SearchParameterInfo(
            name: semanticCode,
            code: semanticCode,
            searchParamType: SearchParamType.Special,
            url: new Uri($"http://ignixa.test/SearchParameter/{targetResourceType}-{semanticCode}"),
            baseResourceTypes: new[] { targetResourceType },
            vectorConfig: vectorConfig);

        context.AddCommon(parameter, targetResourceType);

        return new SearchOptionsBuilderHarness(new SearchOptionsBuilder(context.Parser, context.DefinitionManager), "Observation");
    }

    /// <summary>The real builder behind this harness, for callers that need to hand it to another stage.</summary>
    public ISearchOptionsBuilder Builder => _builder;

    public SearchOptions Build(IReadOnlyList<(string Key, string Value)> parameters, IList<ParameterTrace>? outcomes = null)
    {
        var queryParameters = parameters
            .Select(parameter => new QueryParameter(parameter.Key, parameter.Value))
            .ToList();

        return _builder.Build(_resourceType, queryParameters, outcomes: outcomes);
    }
}
