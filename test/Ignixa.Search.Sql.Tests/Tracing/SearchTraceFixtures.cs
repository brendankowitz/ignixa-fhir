using Ignixa.Abstractions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Search.Sql.Tracing;
using Ignixa.Specification.ValueSets.Normative;
using SortOrder = Ignixa.Search.Expressions.SortOrder;

namespace Ignixa.Search.Sql.Tests.Tracing;

/// <summary>Builds SearchTraceTests's scenarios entirely through <see cref="SearchCompiler.CompileAsync"/>,
/// using a fake <see cref="ISearchOptionsBuilder"/> that hands back a hand-built IR (the same pattern
/// EndToEndCompilationTests uses for Resolve/Lower/Emit) rather than the real parser -- SearchCompiler's own
/// orchestration is what these tests exercise, not parsing.</summary>
internal static class SearchTraceFixtures
{
    public static Task<SearchTrace> TracePatientNameSmithAsync()
    {
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 5),
        };
        var expression = new SearchParameterExpression(nameParam, predicate);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression },
            [new ParameterTrace(0, "name:exact", "Smith", null, null, expression, new ParameterOutcome.Compiled())]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("name:exact", "Smith")], builder, resolver, CancellationToken.None);
    }

    public static Task<SearchTrace> TraceUnregisteredParameterAsync()
    {
        var unknownParam = new SearchParameterInfo("unknown", "unknown", SearchParamType.String, new Uri("http://example.org/fhir/SearchParameter/Patient-unknown"));
        var predicate = new SearchParameterPredicateExpression(unknownParam, SearchComparator.Eq, modifier: null, new StringSearchValue("value"))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 5),
        };
        var expression = new SearchParameterExpression(unknownParam, predicate);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression },
            [new ParameterTrace(0, "unknown", "value", null, null, expression, new ParameterOutcome.Compiled())]);

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("unknown", "value")], builder, resolver, CancellationToken.None);
    }

    /// <summary>Patient?active=true -- a bare leaf predicate, unwrapped in a SearchParameterExpression as the real binder shapes it.</summary>
    public static Task<SearchTrace> TracePatientActiveTrueAsync()
    {
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var predicate = new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 4),
        };
        var expression = new SearchParameterExpression(activeParam, predicate);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression },
            [new ParameterTrace(0, "active", "true", null, null, expression, new ParameterOutcome.Compiled())]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("active", "true")], builder, resolver, CancellationToken.None);
    }

    /// <summary>Observation?code-value-concept=8480-6$high -- a token-token composite.</summary>
    public static Task<SearchTrace> TraceObservationTokenTokenCompositeAsync()
    {
        var compositeParam = new SearchParameterInfo(
            "code-value-concept", "code-value-concept", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-code-value-concept"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));
        var valueParam = new SearchParameterInfo("value-concept", "value-concept", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-concept"));

        var codePredicate = new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 6),
        };
        var valuePredicate = new SearchParameterPredicateExpression(valueParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "high", text: null))
        {
            Span = new SourceSpan(SourceOrigin.Value, 7, 4),
        };

        var expression = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.And,
            [
                new CompositeComponentExpression(codeParam, 0, codePredicate) { Span = codePredicate.Span },
                new CompositeComponentExpression(valueParam, 1, valuePredicate) { Span = valuePredicate.Span },
            ]));

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Observation", Expression = expression },
            [new ParameterTrace(0, "code-value-concept", "8480-6$high", null, null, expression, new ParameterOutcome.Compiled())]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 301;
        resolver.SearchParamIds[codeParam.Url!.ToString()] = 88;
        resolver.SearchParamIds[valueParam.Url!.ToString()] = 89;
        resolver.ResourceTypeIds["Observation"] = 104;

        return SearchCompiler.CompileAsync(
            "Observation", [new QueryParameter("code-value-concept", "8480-6$high")], builder, resolver, CancellationToken.None);
    }

    /// <summary>Patient?organization.name=Acme -- a forward chain.</summary>
    public static Task<SearchTrace> TracePatientOrganizationNameChainAsync()
    {
        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var innerPredicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Acme"))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 4),
        };
        var chain = new ChainedExpression(["Patient"], orgParam, ["Organization"], reversed: false, new SearchParameterExpression(nameParam, innerPredicate));

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = chain },
            [new ParameterTrace(0, "organization.name", "Acme", null, null, chain, new ParameterOutcome.Compiled())]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("organization.name", "Acme")], builder, resolver, CancellationToken.None);
    }

    /// <summary>Patient?active=true&amp;_include=Patient:organization -- a leaf match plus an include stage (which contributes no ParamSource CTE of its own).</summary>
    public static Task<SearchTrace> TracePatientActiveWithIncludeAsync()
    {
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var predicate = new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 4),
        };
        var expression = new SearchParameterExpression(activeParam, predicate);

        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var include = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", ["Organization"], wildCard: false, reversed: false, iterate: false);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression, Include = [include] },
            [new ParameterTrace(0, "active", "true", null, null, expression, new ParameterOutcome.Compiled())]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        return SearchCompiler.CompileAsync(
            "Patient",
            [new QueryParameter("active", "true"), new QueryParameter("_include", "Patient:organization")],
            builder,
            resolver,
            CancellationToken.None);
    }

    /// <summary>Patient?active=true&amp;_sort=name -- a leaf match plus a sort key (which contributes no ParamSource CTE of its own).</summary>
    public static Task<SearchTrace> TracePatientActiveWithSortAsync()
    {
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var predicate = new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 4),
        };
        var expression = new SearchParameterExpression(activeParam, predicate);

        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var sort = new SortExpression(nameParam, SortOrder.Ascending);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression, Sort = [sort] },
            [new ParameterTrace(0, "active", "true", null, null, expression, new ParameterOutcome.Compiled())]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("active", "true"), new QueryParameter("_sort", "name")], builder, resolver, CancellationToken.None);
    }

    /// <summary>Patient?name:not=Smith -- a single-value :not (modifier on the predicate itself, not a NotExpression wrapper).</summary>
    public static Task<SearchTrace> TracePatientNameNotAsync()
    {
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Not), new StringSearchValue("Smith"))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 5),
        };
        var expression = new SearchParameterExpression(nameParam, predicate);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression },
            [new ParameterTrace(0, "name:not", "Smith", null, null, expression, new ParameterOutcome.Compiled())]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("name:not", "Smith")], builder, resolver, CancellationToken.None);
    }

    /// <summary>Patient?name:missing=true -- :missing lowers to a presence ParamSource that is deliberately exempt from CTE provenance.</summary>
    public static Task<SearchTrace> TracePatientNameMissingAsync()
    {
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var missing = new MissingSearchParameterExpression(nameParam, isMissing: true);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = missing },
            [new ParameterTrace(0, "name:missing", "true", null, null, missing, new ParameterOutcome.Compiled())]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("name:missing", "true")], builder, resolver, CancellationToken.None);
    }

    private sealed class FakeSearchOptionsBuilder(SearchOptions options, IReadOnlyList<ParameterTrace> outcomes) : ISearchOptionsBuilder
    {
        public SearchOptions Build(string? resourceType, IReadOnlyList<QueryParameter> parameters, ISchema? schemaProvider = null, IList<ParameterTrace>? outcomeCollector = null)
        {
            if (outcomeCollector is not null)
            {
                foreach (var outcome in outcomes)
                {
                    outcomeCollector.Add(outcome);
                }
            }

            return options;
        }
    }

    private sealed class FakeSymbolResolver : ISymbolResolver
    {
        public Dictionary<string, short> SearchParamIds { get; } = [];

        public Dictionary<string, short> ResourceTypeIds { get; } = [];

        public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
            => Task.FromResult(parameter.Url?.ToString() is { } url && SearchParamIds.TryGetValue(url, out var id) ? (short?)id : null);

        public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
            => Task.FromResult(ResourceTypeIds.TryGetValue(resourceType, out var id) ? (short?)id : null);
    }
}
