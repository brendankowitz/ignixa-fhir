using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Catalog;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

/// <summary>
/// Explain names parameters by reading what emission bound, so any CTE kind whose explain rendering forgets a
/// bound value now throws rather than printing a wrong name. That is the right trade, but it means every kind
/// has to be covered — a gap is a crash on a valid search, not a cosmetic defect.
/// </summary>
/// <remarks>
/// This exists because <c>MultiTypeResourceSource</c> carrying a predicate did exactly that: the emitter bound
/// it, the explainer rendered nothing, and <c>Describe</c> threw on a system-level search compiled with full
/// diagnostics. It survived the whole suite because no test explained that shape. A per-kind theory scales
/// automatically as CTE kinds are added, which a hand-written test per shape does not.
/// </remarks>
public class PlanExplainCoversEveryCteKindTests
{
    public static TheoryData<string, CteDefinition> ParameterBindingCtes()
    {
        var stringTable = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(stringTable.TableName, "Text"), new SqlParameterRef("Smith"));

        return new TheoryData<string, CteDefinition>
        {
            { nameof(CteDefinition.ParamSource), new CteDefinition.ParamSource(stringTable, 103, 202, predicate) },
            { nameof(CteDefinition.ResourceSource), new CteDefinition.ResourceSource(103, predicate) },
            { $"{nameof(CteDefinition.ResourceSource)}(no predicate)", new CteDefinition.ResourceSource(103) },
            { nameof(CteDefinition.MultiTypeResourceSource), CteDefinition.MultiTypeResourceSource.AllTypes(predicate) },
            { $"{nameof(CteDefinition.MultiTypeResourceSource)}(no predicate)", CteDefinition.MultiTypeResourceSource.AllTypes() },
            { nameof(CteDefinition.TableExistsPredicate), new CteDefinition.TableExistsPredicate(stringTable, predicate) },
            { $"{nameof(CteDefinition.TableExistsPredicate)}(no predicate)", new CteDefinition.TableExistsPredicate(stringTable, null) },
            { nameof(CteDefinition.VisibleSinceFilter), new CteDefinition.VisibleSinceFilter(new SqlParameterRef(DateTimeOffset.UnixEpoch)) },
            { nameof(CteDefinition.CompartmentSource), new CteDefinition.CompartmentSource([104], 77, predicate) },
            { nameof(CteDefinition.NotReferencedSource), new CteDefinition.NotReferencedSource(103, null, null) },
        };
    }

    [Theory]
    [MemberData(nameof(ParameterBindingCtes))]
    public void GivenAnyCteKindAsTheMatchRoot_WhenExplained_ThenItAccountsForEveryParameterEmissionBinds(
        string kind,
        CteDefinition cte)
    {
        var plan = new QueryPlan([cte], new MatchPageSpec(new CteRef(0)));

        var emitted = SqlBuilder.Run(plan);

        // Not throwing IS the assertion, and it is a strong one: the cursor rejects a row that names a value
        // emission did not bind at that position, and RequireFullyConsumed rejects a plan whose explain left
        // any bound parameter unaccounted for. Counting @pN tokens would be wrong -- several kinds
        // deliberately consume a parameter while rendering its value inline (ResourceSource[103]).
        Should.NotThrow(
            () => plan.Explain(),
            $"{kind} explained a different set of parameters from the {emitted.Parameters.Count} emission bound");
    }

    [Theory]
    [MemberData(nameof(ParameterBindingCtes))]
    public void GivenAnyCteKindBeneathAStructuralNode_WhenExplained_ThenTheLaterRowsStillAccountForTheirParameters(
        string kind,
        CteDefinition cte)
    {
        // The same kinds again, but not as the root: a structural node above them shifts every later ordinal,
        // which is where a miscounted row does its damage.
        var plan = new QueryPlan(
            [cte, new CteDefinition.ResourceSource(105), new CteDefinition.Intersect(new CteRef(0), new CteRef(1))],
            new MatchPageSpec(new CteRef(2)));

        var emitted = SqlBuilder.Run(plan);
        emitted.Parameters.ShouldNotBeEmpty();

        Should.NotThrow(() => plan.Explain(), $"{kind} diverged from emission when it was not the match root");
    }
}
