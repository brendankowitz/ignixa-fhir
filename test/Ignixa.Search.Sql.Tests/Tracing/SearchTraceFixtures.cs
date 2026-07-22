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
        => TracePatientNameSmithWithTimeProviderAsync(timeProvider: null);

    public static Task<SearchTrace> TracePatientNameSmithWithTimeProviderAsync(TimeProvider? timeProvider)
    {
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 5),
        };
        var expression = new SearchParameterExpression(nameParam, predicate);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression },
            [new ParameterTrace(0, "name:exact", null, "Smith", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("name:exact", "Smith")], builder, resolver,
            timeProvider: timeProvider);
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
            [new ParameterTrace(0, "unknown", null, "value", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("unknown", "value")], builder, resolver);
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
            [new ParameterTrace(0, "active", null, "true", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("active", "true")], builder, resolver);
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
            [new ParameterTrace(0, "code-value-concept", null, "8480-6$high", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 301;
        resolver.SearchParamIds[codeParam.Url!.ToString()] = 88;
        resolver.SearchParamIds[valueParam.Url!.ToString()] = 89;
        resolver.ResourceTypeIds["Observation"] = 104;

        return SearchCompiler.CompileAsync(
            "Observation", [new QueryParameter("code-value-concept", "8480-6$high")], builder, resolver);
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
            [new ParameterTrace(0, "organization.name", null, "Acme", null, chain, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("organization.name", "Acme")], builder, resolver);
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
            [new ParameterTrace(0, "active", null, "true", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        return SearchCompiler.CompileAsync(
            "Patient",
            [new QueryParameter("active", "true"), new QueryParameter("_include", "Patient:organization")],
            builder,
            resolver);
    }

    /// <summary>Patient?name=Smith&amp;_include=Patient:organization where the include's reference parameter is
    /// unregistered -- the builder raises a ParameterTrace for the search parameter only, so nothing owns the
    /// unresolved include and per-parameter attribution cannot report it.</summary>
    public static Task<SearchTrace> TraceUnresolvedIncludeAsync()
    {
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 5),
        };
        var expression = new SearchParameterExpression(nameParam, predicate);

        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var include = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", ["Organization"], wildCard: false, reversed: false, iterate: false);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression, Include = [include] },
            [new ParameterTrace(0, "name", null, "Smith", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        return SearchCompiler.CompileAsync(
            "Patient",
            [new QueryParameter("name", "Smith"), new QueryParameter("_include", "Patient:organization")],
            builder,
            resolver);
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
            [new ParameterTrace(0, "active", null, "true", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("active", "true"), new QueryParameter("_sort", "name")], builder, resolver);
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
            [new ParameterTrace(0, "name:not", null, "Smith", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("name:not", "Smith")], builder, resolver);
    }

    /// <summary>Patient?name:missing=true -- :missing lowers to a presence ParamSource that is deliberately exempt from CTE provenance.</summary>
    public static Task<SearchTrace> TracePatientNameMissingAsync()
    {
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var missing = new MissingSearchParameterExpression(nameParam, isMissing: true);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = missing },
            [new ParameterTrace(0, "name:missing", null, "true", null, missing, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("name:missing", "true")], builder, resolver);
    }

    /// <summary>A resolvable leaf whose value type has no leaf lowering rule, so Lower throws after Resolve succeeds
    /// -- the only shape that reaches SearchCompiler's Lower/Emit failure attribution.</summary>
    public static Task<SearchTrace> TraceUnsupportedLeafValueAsync()
    {
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var unsupportedValue = new CompositeIndexSearchValue([[new StringSearchValue("Smith")]]);
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, unsupportedValue)
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 5),
        };
        var expression = new SearchParameterExpression(nameParam, predicate);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression },
            [new ParameterTrace(0, "name", null, "Smith", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("name", "Smith")], builder, resolver);
    }

    /// <summary>The same unsupported leaf value behind a :not modifier -- Lower rebuilds the predicate as a positive
    /// match before dispatching, so this covers the rebuilt clone carrying the original's span through to attribution.</summary>
    public static Task<SearchTrace> TraceUnsupportedNotLeafValueAsync()
    {
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var unsupportedValue = new CompositeIndexSearchValue([[new StringSearchValue("Smith")]]);
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Not), unsupportedValue)
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 5),
        };
        var expression = new SearchParameterExpression(nameParam, predicate);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression },
            [new ParameterTrace(0, "name:not", null, "Smith", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("name:not", "Smith")], builder, resolver);
    }

    /// <summary>A composite whose component value types have no composite lowering rule, so the composite dispatcher
    /// throws and attributes the failure to its first component's span.</summary>
    public static Task<SearchTrace> TraceUnsupportedCompositeAsync()
    {
        var compositeParam = new SearchParameterInfo(
            "code-value-string", "code-value-string", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-code-value-string"));
        var firstParam = new SearchParameterInfo("value-string", "value-string", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-string"));
        var secondParam = new SearchParameterInfo("value-string-2", "value-string-2", SearchParamType.String, new Uri("http://example.org/fhir/SearchParameter/Observation-value-string-2"));

        var firstPredicate = new SearchParameterPredicateExpression(firstParam, SearchComparator.Eq, modifier: null, new StringSearchValue("a"))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 1),
        };
        var secondPredicate = new SearchParameterPredicateExpression(secondParam, SearchComparator.Eq, modifier: null, new StringSearchValue("b"))
        {
            Span = new SourceSpan(SourceOrigin.Value, 2, 1),
        };

        var expression = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.And,
            [
                new CompositeComponentExpression(firstParam, 0, firstPredicate) { Span = firstPredicate.Span },
                new CompositeComponentExpression(secondParam, 1, secondPredicate) { Span = secondPredicate.Span },
            ]));

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Observation", Expression = expression },
            [new ParameterTrace(0, "code-value-string", null, "a$b", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 302;
        resolver.SearchParamIds[firstParam.Url!.ToString()] = 90;
        resolver.SearchParamIds[secondParam.Url!.ToString()] = 91;
        resolver.ResourceTypeIds["Observation"] = 104;

        return SearchCompiler.CompileAsync(
            "Observation", [new QueryParameter("code-value-string", "a$b")], builder, resolver);
    }

    /// <summary>Two parameters whose values are the same length, so the real parser gives them identical spans, and
    /// only the second can lower. Attribution must follow the parameter, not the span they share.</summary>
    public static Task<SearchTrace> TraceCollidingSpansWithOneFailureAsync()
    {
        var genderParam = new SearchParameterInfo("gender", "gender", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-gender"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var sharedSpan = new SourceSpan(SourceOrigin.Value, 0, 4);

        var genderPredicate = new SearchParameterPredicateExpression(genderParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "male", text: null))
        {
            Span = sharedSpan,
        };
        var namePredicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new CompositeIndexSearchValue([[new StringSearchValue("abcd")]]))
        {
            Span = sharedSpan,
        };

        var genderExpression = new SearchParameterExpression(genderParam, genderPredicate);
        var nameExpression = new SearchParameterExpression(nameParam, namePredicate);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions
            {
                ResourceType = "Patient",
                Expression = new MultiaryExpression(MultiaryOperator.And, [genderExpression, nameExpression]),
            },
            [
                new ParameterTrace(0, "gender", null, "male", null, genderExpression, new ParameterOutcome.Compiled(), null),
                new ParameterTrace(1, "name", null, "abcd", null, nameExpression, new ParameterOutcome.Compiled(), null),
            ]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[genderParam.Url!.ToString()] = 33;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("gender", "male"), new QueryParameter("name", "abcd")], builder, resolver);
    }

    /// <summary>Patient?_id=123 against a resolver holding no _id row -- a resource-column parameter needs no
    /// SearchParamId, so it must not be reported unresolved and must not gate Lower off.</summary>
    public static Task<SearchTrace> TraceResourceColumnIdAsync()
    {
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var predicate = new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "123", text: null))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 3),
        };
        var expression = new SearchParameterExpression(idParam, predicate);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression },
            [new ParameterTrace(0, "_id", null, "123", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("_id", "123")], builder, resolver);
    }

    /// <summary>Patient?organization.name=Acme where the chain's own reference parameter is unregistered -- the
    /// unresolved parameter lives on the ChainedExpression, not on any leaf predicate.</summary>
    public static Task<SearchTrace> TraceUnresolvedChainReferenceParameterAsync()
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
            [new ParameterTrace(0, "organization.name", null, "Acme", null, chain, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("organization.name", "Acme")], builder, resolver);
    }

    /// <summary>Patient?active=true&amp;_sort=a,b,c,d -- the sort-key cap throws from outside both lowering
    /// dispatchers, so the failure names no parameter and exists only on the trace's own Failure.</summary>
    public static Task<SearchTrace> TraceSortKeyCapExceededAsync()
    {
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var predicate = new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 4),
        };
        var expression = new SearchParameterExpression(activeParam, predicate);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.ResourceTypeIds["Patient"] = 103;

        var sorts = new List<SortExpression>();
        foreach (var code in (string[])["a", "b", "c", "d"])
        {
            var sortParam = new SearchParameterInfo(code, code, SearchParamType.String, new Uri($"http://hl7.org/fhir/SearchParameter/Patient-{code}"));
            resolver.SearchParamIds[sortParam.Url!.ToString()] = 500;
            sorts.Add(new SortExpression(sortParam, SortOrder.Ascending));
        }

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression, Sort = sorts },
            [new ParameterTrace(0, "active", null, "true", null, expression, new ParameterOutcome.Compiled(), null)]);

        return SearchCompiler.CompileAsync(
            "Patient", [new QueryParameter("active", "true"), new QueryParameter("_sort", "a,b,c,d")], builder, resolver);
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

        public Dictionary<string, int> SystemIds { get; } = [];

        public Dictionary<string, int> QuantityCodeIds { get; } = [];

        public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
            => Task.FromResult(parameter.Url?.ToString() is { } url && SearchParamIds.TryGetValue(url, out var id) ? (short?)id : null);

        public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
            => Task.FromResult(ResourceTypeIds.TryGetValue(resourceType, out var id) ? (short?)id : null);

        public Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
            => Task.FromResult(SystemIds.TryGetValue(system, out var id) ? (int?)id : null);

        public Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult(QuantityCodeIds.TryGetValue(code, out var id) ? (int?)id : null);
    }
}
