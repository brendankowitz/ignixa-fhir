using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Search.Sql.Tests.Ast;
using Ignixa.Search.Sql.Tracing;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Tracing;

/// <summary>
/// Proves <see cref="SearchCompiler.CompileFromOptionsAsync"/> forwards <see cref="SearchOptions.AccessConstraints"/>
/// into the compiler rather than silently dropping them -- the same fail-open defect this branch's own review
/// caught when it found the property "connected to nothing." The query and the constraint deliberately share
/// <c>statusParam</c> but bind different codes ("final" vs. "amended"): "amended" reaching the emitted SQL
/// can only happen if the constraint's own predicate was lowered.
/// <para>
/// The shared-parameter case alone is not sufficient coverage, which is why
/// <see cref="GivenAConstraintOnAParameterTheQueryDoesNotUse_WhenCompilingFromOptions_ThenItStillCompiles"/>
/// exists: constraints are forwarded to Resolve as well as to Lower, so a constraint predicate naming a
/// parameter the query never mentions still resolves. Before that forwarding existed, this suite passed only
/// because every fixture reused a parameter the query itself already referenced -- the constraint's symbols
/// rode in on the query's coat-tails, and any real SMART scope would have thrown out of Lower.
/// </para>
/// </summary>
public class CompileFromOptionsTests
{
    private const short ObservationTypeId = 104;
    private const short PatientTypeId = 103;
    private const short StatusParamId = 220;

    [Fact]
    public async Task GivenSearchOptionsCarryingAccessConstraints_WhenCompilingFromOptions_ThenTheEmittedSqlEnforcesThem()
    {
        // Arrange -- Observation?status=final, plus an access constraint status=amended on Observation.
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var expression = new SearchParameterExpression(statusParam, TokenPredicateLeaf(statusParam, "final"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[statusParam.Url!.ToString()] = StatusParamId;
        resolver.ResourceTypeIds["Observation"] = ObservationTypeId;

        var options = new SearchOptions { ResourceType = "Observation", Expression = expression };
        options.AccessConstraints = [new AccessConstraint("Observation", TokenPredicate(statusParam, "amended"))];

        // Act
        var trace = await SearchCompiler.CompileFromOptionsAsync(
            options,
            "Observation",
            resolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            cancellationToken: CancellationToken.None);

        // Assert -- the constraint's own bound value ("amended") must reach the emitted SQL: the query
        // itself only ever binds "final", so "amended" appearing at all proves the constraint's predicate
        // was lowered, not merely accepted by the API. Ctes.Count > 1 proves it was intersected into the
        // match set, not lowered and discarded.
        trace.Failure.ShouldBeNull();
        trace.Sql.ShouldNotBeNull();
        trace.Sql!.Parameters.ShouldContain(p => Equals(p.Value, "amended"));
        trace.CompiledPlan.ShouldNotBeNull();
        trace.CompiledPlan!.Ctes.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task GivenAConstraintOnAParameterTheQueryDoesNotUse_WhenCompilingFromOptions_ThenItStillCompiles()
    {
        // Arrange -- Observation?code=1234-5 constrained by status=final. This is the realistic shape: a
        // SMART scope restricts on whatever the policy names, which has no reason to be a parameter the
        // caller happened to search on. The constraint's parameter (status) appears nowhere in the query,
        // so it is collected only if Resolve is given the constraints -- otherwise SymbolTable.SearchParamId
        // throws KeyNotFoundException out of Lower and the whole search fails.
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var expression = new SearchParameterExpression(codeParam, TokenPredicateLeaf(codeParam, "1234-5"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[codeParam.Url!.ToString()] = 221;
        resolver.SearchParamIds[statusParam.Url!.ToString()] = StatusParamId;
        resolver.ResourceTypeIds["Observation"] = ObservationTypeId;

        var options = new SearchOptions { ResourceType = "Observation", Expression = expression };
        options.AccessConstraints = [new AccessConstraint("Observation", TokenPredicate(statusParam, "final"))];

        // Act
        var trace = await SearchCompiler.CompileFromOptionsAsync(
            options,
            "Observation",
            resolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            cancellationToken: CancellationToken.None);

        // Assert -- compiles, and the constraint is genuinely enforced rather than dropped to make it
        // compile: the constraint's parameter id and its bound value both reach the emitted SQL.
        trace.Failure.ShouldBeNull();
        trace.Sql.ShouldNotBeNull();
        trace.Sql!.Sql.ShouldContain($"SearchParamId = {StatusParamId}");
        trace.Sql.Parameters.ShouldContain(p => Equals(p.Value, "final"));
    }

    [Fact]
    public async Task GivenAConstraintOnATypeTheQueryDoesNotName_WhenCompilingFromOptions_ThenThatTypeResolves()
    {
        // Arrange -- Patient?_id=abc with a constraint governing Observation, reachable through a
        // _revinclude. The constrained type is named only by the constraint, so ApplyToTypes'
        // LowerResourceSource("Observation") finds no id unless Resolve collected it from the constraint.
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[statusParam.Url!.ToString()] = StatusParamId;
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 230;
        resolver.ResourceTypeIds["Patient"] = PatientTypeId;
        resolver.ResourceTypeIds["Observation"] = ObservationTypeId;

        var options = new SearchOptions { ResourceType = "Patient" };
        options.RevInclude = [new IncludeExpression(["Observation"], subjectParam, "Observation", "Patient", referencedTypes: null, wildCard: false, reversed: true, iterate: false)];
        options.AccessConstraints = [new AccessConstraint("Observation", TokenPredicate(statusParam, "final"))];

        // Act
        var trace = await SearchCompiler.CompileFromOptionsAsync(
            options,
            "Patient",
            resolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            cancellationToken: CancellationToken.None);

        // Assert
        trace.Failure.ShouldBeNull();
        trace.Sql.ShouldNotBeNull();
        trace.Sql!.Sql.ShouldContain($"SearchParamId = {StatusParamId}");
    }

    [Fact]
    public async Task GivenSearchOptionsCarryingResourceTypes_WhenCompilingFromOptions_ThenTheEmittedSqlNarrowsToThem()
    {
        // Arrange -- GET /?_type=Observation,Patient&status=final. resourceType is null (system-level), so
        // the status leaf lowers with no ResourceTypeId of its own; the requested types are the only thing
        // that can narrow the result. Deleting the ResourceTypes forwarding in CompileFromOptionsAsync makes
        // the IN list vanish and this test fail -- the whole point, since a dropped _type silently returns
        // every resource type rather than erroring.
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var expression = new SearchParameterExpression(statusParam, TokenPredicateLeaf(statusParam, "final"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[statusParam.Url!.ToString()] = StatusParamId;
        resolver.ResourceTypeIds["Observation"] = ObservationTypeId;
        resolver.ResourceTypeIds["Patient"] = PatientTypeId;

        var options = new SearchOptions
        {
            Expression = expression,
            ResourceTypes = ["Observation", "Patient"],
        };

        // Act
        var trace = await SearchCompiler.CompileFromOptionsAsync(
            options,
            resourceType: null,
            resolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            cancellationToken: CancellationToken.None);

        // Assert -- the real ids, not the unmatchable sentinel: the requested types must reach the symbol
        // resolver too, not only Lower. An IN (-1, -1) would compile, emit, and match nothing at all.
        trace.Failure.ShouldBeNull();
        trace.Sql.ShouldNotBeNull();
        trace.Sql!.Sql.ShouldContain($"ResourceTypeId IN ({ObservationTypeId}, {PatientTypeId})");
    }

    [Fact]
    public async Task GivenSearchOptionsCarryingHistoryResourceVersionTypes_WhenCompilingFromOptions_ThenTheEmittedSqlFiltersToSupersededRows()
    {
        // Arrange -- GET /?_type=Observation&status=final, ResourceVersionTypes=History. Under the tri-state
        // visibility model History alone (Latest absent) pins IsHistory to the non-current partition, so the
        // emitted scan filters to "IsHistory = 1" -- superseded versions exclusively -- rather than the
        // Latest UNION History the earlier relaxation-only model produced. IsDeleted is left unconstrained
        // (neither Latest nor SoftDeleted pins it). System-level (resourceType: null) so the base set lowers
        // to a MultiTypeResourceSource, which renders its own visibility check directly against dbo.Resource
        // -- unlike a typed ParamSource against a search-param table, which only carries an IsHistory clause
        // when that specific table has the column (dbo.TokenSearchParam does not), so asserting against a
        // ParamSource-only plan would pass whether or not Visibility is forwarded. Same class of defect as
        // AccessConstraints/ResourceTypes above: without forwarding, ResourceVersionTypes is accepted by
        // the API but never reaches Lower, so the emitted SQL keeps filtering to "IsHistory = 0" -- silent
        // Latest-only results for a caller that asked for history.
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var expression = new SearchParameterExpression(statusParam, TokenPredicateLeaf(statusParam, "final"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[statusParam.Url!.ToString()] = StatusParamId;
        resolver.ResourceTypeIds["Observation"] = ObservationTypeId;

        var options = new SearchOptions
        {
            Expression = expression,
            ResourceTypes = ["Observation"],
            ResourceVersionTypes = ResourceVersionTypes.History,
        };

        // Act
        var trace = await SearchCompiler.CompileFromOptionsAsync(
            options,
            resourceType: null,
            resolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            cancellationToken: CancellationToken.None);

        // Assert -- the MultiTypeResourceSource scan must select superseded rows (IsHistory = 1) and must not
        // filter them out (IsHistory = 0). Deleting the Visibility forwarding in CompileFromOptionsAsync
        // restores "IsHistory = 0" to the emitted SQL (EmitMultiTypeResourceSource) and fails this.
        trace.Failure.ShouldBeNull();
        trace.Sql.ShouldNotBeNull();
        trace.Sql!.Sql.ShouldContain($"ResourceTypeId IN ({ObservationTypeId})");
        trace.Sql!.Sql.ShouldContain("IsHistory = 1");
        trace.Sql!.Sql.ShouldNotContain("IsHistory = 0");
    }

    [Fact]
    public async Task GivenSearchOptionsWithNoneResourceVersionTypes_WhenCompilingFromOptions_ThenTheTraceRecordsALowerStageFailure()
    {
        // Arrange -- None is not a valid search input (SearchOptions.ResourceVersionTypes' own doc); the
        // compiler must reject it rather than silently treating it as Latest, which would reproduce the
        // exact fail-open-by-omission shape this forwarding exists to close. This class's own contract
        // (see the class doc) is that a NotSupportedException at this boundary is recorded on
        // SearchTrace.Failure rather than thrown past CompileFromOptionsAsync -- the same convention
        // AccessConstraints/ResourceTypes guards already follow.
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var expression = new SearchParameterExpression(statusParam, TokenPredicateLeaf(statusParam, "final"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[statusParam.Url!.ToString()] = StatusParamId;
        resolver.ResourceTypeIds["Observation"] = ObservationTypeId;

        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = expression,
            ResourceVersionTypes = ResourceVersionTypes.None,
        };

        // Act
        var trace = await SearchCompiler.CompileFromOptionsAsync(
            options,
            "Observation",
            resolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            cancellationToken: CancellationToken.None);

        // Assert
        trace.Failure.ShouldNotBeNull();
        trace.Failure!.Stage.ShouldBe(TraceStage.Lower);
        trace.Sql.ShouldBeNull();
    }

    [Fact]
    public async Task GivenSearchOptionsCarryingOnlySurrogateBounds_WhenCompilingFromOptions_ThenTheEmittedSqlAppliesTheRange()
    {
        // Arrange -- no explicit surrogateIdRange method argument (an $export worker's path); the bound
        // travels only through SearchOptions.StartSurrogateId/EndSurrogateId, the path a caller reaching
        // this compiler through ISearchService would use. Deleting the fallback in ToSurrogateRange makes
        // this SearchOptions pair vanish and the m.Sid1 clauses (and their bound values) never appear.
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var expression = new SearchParameterExpression(statusParam, TokenPredicateLeaf(statusParam, "final"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[statusParam.Url!.ToString()] = StatusParamId;
        resolver.ResourceTypeIds["Observation"] = ObservationTypeId;

        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = expression,
            StartSurrogateId = 5000L,
            EndSurrogateId = 6000L,
        };

        // Act
        var trace = await SearchCompiler.CompileFromOptionsAsync(
            options,
            "Observation",
            resolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            cancellationToken: CancellationToken.None);

        // Assert -- both the bound predicate against m.Sid1 and the caller's own values (5000/6000) must
        // reach the emitted statement. Neither can appear unless CompileFromOptionsAsync read the
        // SearchOptions properties itself, since no explicit surrogateIdRange argument was supplied.
        trace.Failure.ShouldBeNull();
        trace.Sql.ShouldNotBeNull();
        trace.Sql!.Sql.ShouldContain("m.Sid1 >=");
        trace.Sql!.Sql.ShouldContain("m.Sid1 <=");
        trace.Sql!.Parameters.ShouldContain(p => Equals(p.Value, 5000L));
        trace.Sql!.Parameters.ShouldContain(p => Equals(p.Value, 6000L));
    }

    [Fact]
    public async Task GivenSearchOptionsWithOnlyOneSurrogateBoundSet_WhenCompilingFromOptions_ThenTheTraceRecordsALowerStageFailure()
    {
        // Arrange -- StartSurrogateId set, EndSurrogateId left null. A half-open range is a caller error,
        // not a partial intent to honour: silently treating the unset bound as unbounded would scan outside
        // the caller's intended partition, the same fail-open shape this forwarding exists to close.
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var expression = new SearchParameterExpression(statusParam, TokenPredicateLeaf(statusParam, "final"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[statusParam.Url!.ToString()] = StatusParamId;
        resolver.ResourceTypeIds["Observation"] = ObservationTypeId;

        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = expression,
            StartSurrogateId = 5000L,
        };

        // Act
        var trace = await SearchCompiler.CompileFromOptionsAsync(
            options,
            "Observation",
            resolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            cancellationToken: CancellationToken.None);

        // Assert
        trace.Failure.ShouldNotBeNull();
        trace.Failure!.Stage.ShouldBe(TraceStage.Lower);
        trace.Sql.ShouldBeNull();
    }

    [Fact]
    public async Task GivenSearchOptionsCarryingAllowedResourceTypes_WhenCompilingFromOptions_ThenTheEmittedSqlEnforcesThem()
    {
        // Arrange -- Patient?_revinclude=Observation:subject, with an allow-list of only Patient. The
        // revinclude produces Observation, which the scope does not grant, so its output-type filter must
        // collapse to the unmatchable sentinel. Deleting the AllowedResourceTypes forwarding in
        // CompileFromOptionsAsync leaves the stage producing Observation (rsp.ResourceTypeId = 104) and the
        // match ungated -- the exact fail-open bypass this forwarding exists to close: an _include returning
        // a resource type the SMART scope never permitted.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 230;
        resolver.ResourceTypeIds["Patient"] = PatientTypeId;
        resolver.ResourceTypeIds["Observation"] = ObservationTypeId;

        var options = new SearchOptions
        {
            ResourceType = "Patient",
            RevInclude = [new IncludeExpression(["Observation"], subjectParam, "Observation", "Patient", referencedTypes: null, wildCard: false, reversed: true, iterate: false)],
            AllowedResourceTypes = ["Patient"],
        };

        // Act
        var trace = await SearchCompiler.CompileFromOptionsAsync(
            options,
            "Patient",
            resolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            cancellationToken: CancellationToken.None);

        // Assert -- the match is gated to the allowed base set (ResourceTypeId IN (103)), and the disallowed
        // revinclude produces no rows: its output filter is the unmatchable "rsp.ResourceTypeId = -1", never
        // the Observation type id. Both can only appear if AllowedResourceTypes reached Lower.
        trace.Failure.ShouldBeNull();
        trace.Sql.ShouldNotBeNull();
        trace.Sql!.Sql.ShouldContain($"ResourceTypeId IN ({PatientTypeId})");
        trace.Sql!.Sql.ShouldContain("rsp.ResourceTypeId = -1");
        trace.Sql!.Sql.ShouldNotContain($"rsp.ResourceTypeId = {ObservationTypeId}");
    }

    // One case per row of the ResourceVersionTypes -> visibility truth table (mirrors the legacy generator's
    // AppendHistoryClause/AppendDeletedClause). Driven end-to-end through the public API so both ToVisibility
    // (the mapping) and EmitMultiTypeResourceSource (the rendering) are exercised. A system-level search
    // (resourceType null, ResourceTypes set) lowers to a MultiTypeResourceSource, which renders BOTH version
    // columns directly against dbo.Resource -- the only base-set shape that lets a single assertion see the
    // IsDeleted axis at all. Each row asserts both presence of the predicate it expects AND absence of the
    // opposite value: asserting only presence would pass against an emitter that wrote both "= 0" and "= 1".
    [Theory]
    [InlineData(ResourceVersionTypes.Latest, "IsHistory = 0", "IsDeleted = 0")]
    [InlineData(ResourceVersionTypes.History, "IsHistory = 1", null)]
    [InlineData(ResourceVersionTypes.SoftDeleted, null, "IsDeleted = 1")]
    [InlineData(ResourceVersionTypes.Latest | ResourceVersionTypes.History, null, "IsDeleted = 0")]
    [InlineData(ResourceVersionTypes.Latest | ResourceVersionTypes.SoftDeleted, "IsHistory = 0", null)]
    [InlineData(ResourceVersionTypes.History | ResourceVersionTypes.SoftDeleted, "IsHistory = 1", "IsDeleted = 1")]
    [InlineData(ResourceVersionTypes.Latest | ResourceVersionTypes.History | ResourceVersionTypes.SoftDeleted, null, null)]
    public async Task GivenSearchOptionsCarryingEachResourceVersionType_WhenCompilingFromOptions_ThenTheEmittedSqlMatchesTheLegacyTruthTable(
        ResourceVersionTypes versionTypes, string? expectedHistoryClause, string? expectedDeletedClause)
    {
        // Arrange -- a system-level (resourceType: null) search whose base set lowers to MultiTypeResourceSource.
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var expression = new SearchParameterExpression(statusParam, TokenPredicateLeaf(statusParam, "final"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[statusParam.Url!.ToString()] = StatusParamId;
        resolver.ResourceTypeIds["Observation"] = ObservationTypeId;

        var options = new SearchOptions
        {
            Expression = expression,
            ResourceTypes = ["Observation"],
            ResourceVersionTypes = versionTypes,
        };

        // Act
        var trace = await SearchCompiler.CompileFromOptionsAsync(
            options,
            resourceType: null,
            resolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            cancellationToken: CancellationToken.None);

        // Assert
        trace.Failure.ShouldBeNull();
        trace.Sql.ShouldNotBeNull();
        var sql = trace.Sql!.Sql;

        AssertAxis(sql, "IsHistory", expectedHistoryClause);
        AssertAxis(sql, "IsDeleted", expectedDeletedClause);

        static void AssertAxis(string sql, string column, string? expectedClause)
        {
            if (expectedClause is null)
            {
                // Axis unconstrained: neither the current-row nor the non-current-row predicate may appear.
                sql.ShouldNotContain($"{column} = 0");
                sql.ShouldNotContain($"{column} = 1");
            }
            else
            {
                // Exactly the expected value; the opposite value must be absent so a both-clauses emitter fails.
                var opposite = expectedClause.EndsWith("0", StringComparison.Ordinal) ? $"{column} = 1" : $"{column} = 0";
                sql.ShouldContain(expectedClause);
                sql.ShouldNotContain(opposite);
            }
        }
    }

    // A history-only and a soft-deleted-only search -- the two shapes the earlier relaxation-only visibility
    // could not express at all -- must still produce syntactically valid T-SQL once a search-parameter
    // predicate and an _include are folded in (the include stage and the terminal join both scan dbo.Resource,
    // so a malformed visibility filter would surface as a parse error only in the assembled statement).
    [Theory]
    [InlineData(ResourceVersionTypes.History)]
    [InlineData(ResourceVersionTypes.SoftDeleted)]
    public async Task GivenAHistoryOnlyOrDeletedOnlySearchWithAPredicateAndAnInclude_WhenCompilingFromOptions_ThenTheEmittedSqlIsValidTSql(
        ResourceVersionTypes versionTypes)
    {
        // Arrange -- Observation?status=final&_include=Observation:subject, at type level so the base set is a
        // typed ParamSource (search-param predicate) and the include stage scans dbo.Resource under visibility.
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var expression = new SearchParameterExpression(statusParam, TokenPredicateLeaf(statusParam, "final"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[statusParam.Url!.ToString()] = StatusParamId;
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 230;
        resolver.ResourceTypeIds["Observation"] = ObservationTypeId;
        resolver.ResourceTypeIds["Patient"] = PatientTypeId;

        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = expression,
            Include = [new IncludeExpression(["Observation"], subjectParam, "Observation", "Patient", referencedTypes: null, wildCard: false, reversed: false, iterate: false)],
            ResourceVersionTypes = versionTypes,
        };

        // Act
        var trace = await SearchCompiler.CompileFromOptionsAsync(
            options,
            "Observation",
            resolver,
            compartmentDefinitionManager: null,
            searchParameterDefinitionManager: null,
            timeProvider: null,
            cancellationToken: CancellationToken.None);

        // Assert -- it compiled, and the assembled statement parses as valid T-SQL.
        trace.Failure.ShouldBeNull();
        trace.Sql.ShouldNotBeNull();
        SqlGrammar.AssertValid(trace.Sql!.Sql);
    }

    /// <summary>A wrapped token predicate ("&lt;param&gt; eq &lt;code&gt;"), the shape a real bound leaf takes -- mirrors AccessConstraintTests' TokenPredicate.</summary>
    private static Expression TokenPredicate(SearchParameterInfo parameter, string code)
        => new SearchParameterExpression(parameter, TokenPredicateLeaf(parameter, code));

    private static SearchParameterPredicateExpression TokenPredicateLeaf(SearchParameterInfo parameter, string code)
        => new(parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: code, text: null));

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
