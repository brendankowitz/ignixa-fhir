using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class StructuralContextExceptTests
{
    [Fact]
    public void GivenTwoCteRefs_WhenExcepted_ThenAddsAnExceptCteAndReturnsItsRef()
    {
        // Arrange -- mirrors LowerTests.cs's "GivenAMissingTrueOnAStringParameter..." pattern (the only
        // established pattern for asserting a CteDefinition.Except shape), but drives StructuralContext's
        // own Except method directly rather than going through Lower.Run/:missing.
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });
        var context = new StructuralContext(symbols);
        var left = context.LowerResourceSource("Patient");
        var right = context.LowerResourceSource("Patient");

        // Act
        var result = context.Except(left, right);

        // Assert
        var plan = new QueryPlan(context.Ctes, result);
        plan.Ctes[result.Index].ShouldBeOfType<CteDefinition.Except>();
        var exceptCte = (CteDefinition.Except)plan.Ctes[result.Index];
        exceptCte.Left.ShouldBe(left);
        exceptCte.Right.ShouldBe(right);
    }
}
