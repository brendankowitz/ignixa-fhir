using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Search.Sql.Tests.TestSupport;
using Ignixa.Specification.ValueSets.Normative;
using SortOrder = Ignixa.Search.Expressions.SortOrder;

namespace Ignixa.Search.Sql.Tests.Compilation;

/// <summary>Builds SearchPlanDiagnosticsTests's scenarios entirely through <see cref="SearchSqlCompiler.TryCreatePlanAsync"/>,
/// using a fake <see cref="ISearchOptionsBuilder"/> that hands back a hand-built IR (the same pattern
/// EndToEndCompilationTests uses for Resolve/Lower/Emit) rather than the real parser -- SearchSqlCompiler's own
/// orchestration is what these tests exercise, not parsing.</summary>
internal static class CompilationFixtures
{
    private static readonly SearchPlanOptions FullDiagnostics = new() { DiagnosticsLevel = SearchDiagnosticsLevel.Full };

    public static Task<SearchPlanResult> TracePatientNameSmithAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("name:exact", "Smith")], FullDiagnostics);
    }

    /// <summary>Observation?status=final compiled with an AccessConstraint("Observation", status eq amended)
    /// set on the options. The constraint reuses the status parameter the query already resolves, so it lowers
    /// through SearchSqlCompiler's own Resolve stage; wiring it into Lower.Run intersects the match set with the
    /// constraint, which is what proves <see cref="SearchOptions.AccessConstraints"/> is forwarded rather than
    /// silently ignored. Without the forward the match is a bare ParamSource and no Intersect appears.</summary>
    public static Task<SearchPlanResult> TraceObservationStatusWithAccessConstraintAsync()
    {
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var predicate = new SearchParameterPredicateExpression(statusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "final", text: null))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 5),
        };
        var expression = new SearchParameterExpression(statusParam, predicate);

        // The constraint reuses statusParam (a different code, "amended") so it resolves through the query's
        // own symbols yet still adds a distinct ParamSource, guaranteeing a real Intersect when wired.
        var constraintPredicate = new SearchParameterExpression(
            statusParam,
            new SearchParameterPredicateExpression(statusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "amended", text: null)));
        var constraint = new AccessConstraint("Observation", constraintPredicate);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Observation", Expression = expression, AccessConstraints = [constraint] },
            [new ParameterTrace(0, "status", null, "final", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[statusParam.Url!.ToString()] = 220;
        resolver.ResourceTypeIds["Observation"] = 104;

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Observation", [new QueryParameter("status", "final")], FullDiagnostics);
    }

    public static Task<SearchPlanResult> TracePatientNameSmithWithTimeProviderAsync(TimeProvider? timeProvider)
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

        return new SearchSqlCompiler(resolver, builder, timeProvider: timeProvider).TryCreatePlanAsync(
            "Patient", [new QueryParameter("name:exact", "Smith")], FullDiagnostics);
    }

    /// <summary>Observation?date=ap2020-01-01T00:00:00Z -- a date :ap search, whose lowering actually
    /// depends on the captured reference instant reaching the leaf context's approximation reference time
    /// (unlike <see cref="TracePatientNameSmithWithTimeProviderAsync"/> above, whose plain string value never
    /// reads it at all) -- proving the SearchSqlCompiler boundary's single captured <c>GetUtcNow()</c> value is
    /// what a :ap comparator actually consumes, not just that the call count is right for a query that
    /// wouldn't notice either way. widened = [2019-12-31T21:36:00Z, 2020-01-01T02:24:00Z] for a reference
    /// instant one day after the value (the same scenario already pinned against
    /// EndToEndCompilationTests.GivenADateApComparatorQueryWithAMovingClock... and
    /// DateTimeLoweringRuleTests' "past instant" :ap case).</summary>
    public static Task<SearchPlanResult> TraceObservationDateApWithTimeProviderAsync(TimeProvider? timeProvider)
    {
        var dateParam = new SearchParameterInfo("date", "date", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Observation-date"));
        var value = new DateTimeSearchValue(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var predicate = new SearchParameterPredicateExpression(dateParam, SearchComparator.Ap, modifier: null, value)
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 22),
        };
        var expression = new SearchParameterExpression(dateParam, predicate);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Observation", Expression = expression },
            [new ParameterTrace(0, "date", null, "ap2020-01-01T00:00:00Z", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[dateParam.Url!.ToString()] = 203;
        resolver.ResourceTypeIds["Observation"] = 104;

        return new SearchSqlCompiler(resolver, builder, timeProvider: timeProvider).TryCreatePlanAsync(
            "Observation", [new QueryParameter("date", "ap2020-01-01T00:00:00Z")], FullDiagnostics);
    }

    public static Task<SearchPlanResult> TracePatientNameSmithWithCancellationTokenAsync(CancellationToken cancellationToken)
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("name:exact", "Smith")], FullDiagnostics, cancellationToken);
    }

    public static Task<SearchPlanResult> TracePatientNameSmithWithCancellationCheckAsync(CancellationToken cancellationToken)
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

        var resolver = new CancellationCheckingResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("name:exact", "Smith")], FullDiagnostics, cancellationToken);
    }

    public static Task<SearchPlanResult> TraceUnregisteredParameterAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("unknown", "value")], FullDiagnostics);
    }

    /// <summary>Patient?active=true -- a bare leaf predicate, unwrapped in a SearchParameterExpression as the real binder shapes it.</summary>
    public static Task<SearchPlanResult> TracePatientActiveTrueAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("active", "true")], FullDiagnostics);
    }

    /// <summary>Observation?code-value-concept=8480-6$high -- a token-token composite.</summary>
    public static Task<SearchPlanResult> TraceObservationTokenTokenCompositeAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Observation", [new QueryParameter("code-value-concept", "8480-6$high")], FullDiagnostics);
    }

    /// <summary>Patient?organization.name=Acme -- a forward chain.</summary>
    public static Task<SearchPlanResult> TracePatientOrganizationNameChainAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("organization.name", "Acme")], FullDiagnostics);
    }

    /// <summary>Patient?active=true&amp;_include=Patient:organization -- a leaf match plus an include stage (which contributes no ParamSource CTE of its own).</summary>
    public static Task<SearchPlanResult> TracePatientActiveWithIncludeAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient",
            [new QueryParameter("active", "true"), new QueryParameter("_include", "Patient:organization")], FullDiagnostics);
    }

    /// <summary>Patient?name=Smith&amp;_include=Patient:organization where the include's reference parameter is
    /// unregistered -- the builder raises a ParameterTrace for the search parameter only, so nothing owns the
    /// unresolved include and per-parameter attribution cannot report it.</summary>
    public static Task<SearchPlanResult> TraceUnresolvedIncludeAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient",
            [new QueryParameter("name", "Smith"), new QueryParameter("_include", "Patient:organization")], FullDiagnostics);
    }

    /// <summary>Patient?active=true&amp;_sort=name -- a leaf match plus a sort key (which contributes no ParamSource CTE of its own).</summary>
    public static Task<SearchPlanResult> TracePatientActiveWithSortAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("active", "true"), new QueryParameter("_sort", "name")], FullDiagnostics);
    }

    /// <summary>Patient?name:not=Smith -- a single-value :not (modifier on the predicate itself, not a NotExpression wrapper).</summary>
    public static Task<SearchPlanResult> TracePatientNameNotAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("name:not", "Smith")], FullDiagnostics);
    }

    /// <summary>Patient?name:missing=true -- :missing lowers to a presence ParamSource that is deliberately exempt from CTE provenance.</summary>
    public static Task<SearchPlanResult> TracePatientNameMissingAsync()
    {
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var missing = new MissingSearchParameterExpression(nameParam, isMissing: true);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = missing },
            [new ParameterTrace(0, "name:missing", null, "true", null, missing, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("name:missing", "true")], FullDiagnostics);
    }

    /// <summary>A resolvable leaf whose value type has no leaf lowering rule, so Lower throws after Resolve succeeds
    /// -- the only shape that reaches SearchSqlCompiler's Lower/Emit failure attribution.</summary>
    public static Task<SearchPlanResult> TraceUnsupportedLeafValueAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("name", "Smith")], FullDiagnostics);
    }

    /// <summary>The same unsupported leaf value behind a :not modifier -- Lower rebuilds the predicate as a positive
    /// match before dispatching, so this covers the rebuilt clone carrying the original's span through to attribution.</summary>
    public static Task<SearchPlanResult> TraceUnsupportedNotLeafValueAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("name:not", "Smith")], FullDiagnostics);
    }

    /// <summary>A composite whose component value types have no composite lowering rule, so the composite dispatcher
    /// throws and attributes the failure to its first component's span.</summary>
    public static Task<SearchPlanResult> TraceUnsupportedCompositeAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Observation", [new QueryParameter("code-value-string", "a$b")], FullDiagnostics);
    }

    /// <summary>Two parameters whose values are the same length, so the real parser gives them identical spans, and
    /// only the second can lower. Attribution must follow the parameter, not the span they share.</summary>
    public static Task<SearchPlanResult> TraceCollidingSpansWithOneFailureAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("gender", "male"), new QueryParameter("name", "abcd")], FullDiagnostics);
    }

    /// <summary>Patient?_id=123 against a resolver holding no _id row -- a resource-column parameter needs no
    /// SearchParamId, so it must not be reported unresolved and must not gate Lower off.</summary>
    public static Task<SearchPlanResult> TraceResourceColumnIdAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("_id", "123")], FullDiagnostics);
    }

    /// <summary>Patient?organization.name=Acme where the chain's own reference parameter is unregistered -- the
    /// unresolved parameter lives on the ChainedExpression, not on any leaf predicate.</summary>
    public static Task<SearchPlanResult> TraceUnresolvedChainReferenceParameterAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("organization.name", "Acme")], FullDiagnostics);
    }

    /// <summary>Patient?active=true&amp;_sort=a,b,c,d -- the sort-key cap throws from outside both lowering
    /// dispatchers, so the failure names no parameter and exists only on the trace's own Failure.</summary>
    public static Task<SearchPlanResult> TraceSortKeyCapExceededAsync()
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

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("active", "true"), new QueryParameter("_sort", "a,b,c,d")], FullDiagnostics);
    }

    /// <summary>Patient?identifier=http://unknown.org/mrn|12345 -- the system resolves to nothing, so the
    /// token lowers to Predicate.False and the parameter is a known miss rather than a compile failure.</summary>
    public static Task<SearchPlanResult> TraceUnresolvableTokenSystemAsync()
    {
        var identifierParam = new SearchParameterInfo("identifier", "identifier", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        var predicate = new SearchParameterPredicateExpression(
            identifierParam, SearchComparator.Eq, modifier: null,
            new TokenSearchValue("http://unknown.org/mrn", code: "12345", text: null))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 31),
        };
        var expression = new SearchParameterExpression(identifierParam, predicate);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression },
            [new ParameterTrace(0, "identifier", null, "http://unknown.org/mrn|12345", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[identifierParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient", [new QueryParameter("identifier", "http://unknown.org/mrn|12345")], FullDiagnostics);
    }

    /// <summary>Patient?active=true&amp;identifier=http://unknown.org/mrn|12345 -- one satisfiable parameter
    /// alongside a known miss, so the miss must be attributed to its own parameter and not to both.</summary>
    public static Task<SearchPlanResult> TraceSatisfiableAndUnresolvableTokenSystemAsync()
    {
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var identifierParam = new SearchParameterInfo("identifier", "identifier", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"));

        var activePredicate = new SearchParameterPredicateExpression(
            activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 4),
        };
        var identifierPredicate = new SearchParameterPredicateExpression(
            identifierParam, SearchComparator.Eq, modifier: null,
            new TokenSearchValue("http://unknown.org/mrn", code: "12345", text: null))
        {
            Span = new SourceSpan(SourceOrigin.Value, 10, 28),
        };

        var activeExpression = new SearchParameterExpression(activeParam, activePredicate);
        var identifierExpression = new SearchParameterExpression(identifierParam, identifierPredicate);
        var tree = new MultiaryExpression(MultiaryOperator.And, [activeExpression, identifierExpression]);

        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = tree },
            [
                new ParameterTrace(0, "active", null, "true", null, activeExpression, new ParameterOutcome.Compiled(), null),
                new ParameterTrace(1, "identifier", null, "http://unknown.org/mrn|12345", null, identifierExpression, new ParameterOutcome.Compiled(), null),
            ]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.SearchParamIds[identifierParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;

        return new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient",
            [new QueryParameter("active", "true"), new QueryParameter("identifier", "http://unknown.org/mrn|12345")], FullDiagnostics);
    }

    private sealed class CancellationCheckingResolver : ISymbolResolver
    {
        public Dictionary<string, short> SearchParamIds { get; } = [];

        public Dictionary<string, short> ResourceTypeIds { get; } = [];

        public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(parameter.Url?.ToString() is { } url && SearchParamIds.TryGetValue(url, out var id) ? (short?)id : null);
        }

        public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ResourceTypeIds.TryGetValue(resourceType, out var id) ? (short?)id : null);
        }

        public Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<int?>(null);
        }

        public Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<int?>(null);
        }
    }
}
