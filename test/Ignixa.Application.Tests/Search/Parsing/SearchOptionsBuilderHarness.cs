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

    private SearchOptionsBuilderHarness(SearchOptionsBuilder builder)
    {
        _builder = builder;
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

    public SearchOptions Build(IReadOnlyList<(string Key, string Value)> parameters, IList<ParameterTrace>? outcomes = null)
    {
        var queryParameters = parameters
            .Select(parameter => new QueryParameter(parameter.Key, parameter.Value))
            .ToList();

        return _builder.Build("Patient", queryParameters, outcomes: outcomes);
    }
}
