using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Search.Sql.Tracing;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Tracing;

/// <summary>
/// Proves <see cref="SearchCompiler.CompileFromOptionsAsync"/> forwards <see cref="SearchOptions.AccessConstraints"/>
/// into the compiler rather than silently dropping them -- the same fail-open defect this branch's own review
/// caught when it found the property "connected to nothing." The query and the constraint deliberately share
/// <c>statusParam</c> but bind different codes ("final" vs. "amended"): the query alone resolves the parameter
/// through <see cref="SearchCompiler.CompileFromOptionsAsync"/>'s Resolve stage (AccessConstraints are not
/// themselves symbol-collected -- the constraint's parameter only resolves because the query already
/// references it), and "amended" reaching the emitted SQL can only happen if the constraint's own predicate
/// was lowered.
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
    public async Task GivenSearchOptionsCarryingHistoryResourceVersionTypes_WhenCompilingFromOptions_ThenTheEmittedSqlOmitsTheIsHistoryFilter()
    {
        // Arrange -- GET /?_type=Observation&status=final, ResourceVersionTypes=History (superseded
        // versions only, no Latest). System-level (resourceType: null) so the base set lowers to a
        // MultiTypeResourceSource, which renders its own visibility check directly against dbo.Resource --
        // unlike a typed ParamSource against a search-param table, which only carries an IsHistory clause
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

        // Assert -- the MultiTypeResourceSource scan must not filter out superseded rows. Deleting the
        // Visibility forwarding in CompileFromOptionsAsync restores "IsHistory = 0" to the emitted SQL
        // (EmitMultiTypeResourceSource) and fails this.
        trace.Failure.ShouldBeNull();
        trace.Sql.ShouldNotBeNull();
        trace.Sql!.Sql.ShouldContain($"ResourceTypeId IN ({ObservationTypeId})");
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
