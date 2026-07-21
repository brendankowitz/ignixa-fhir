using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class CompartmentLoweringRuleTests
{
    [Fact]
    public void GivenAGroupedMembershipParameter_WhenLowered_ThenProducesACompartmentSourceWithTheReferencePredicate()
    {
        // Arrange
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [subjectParam.Url.ToString()] = 77 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104, ["Condition"] = 106 });
        var context = new LeafContext(symbols);

        // Act
        var cte = CompartmentLoweringRule.Lower(subjectParam, ["Observation", "Condition"], "Patient", "123", context);

        // Assert
        cte.SearchParamId.ShouldBe((short)77);
        cte.ResourceTypeIds.ShouldBe([(short)104, (short)106]);
        cte.Predicate.ShouldBeOfType<Predicate.And>();
    }
}
