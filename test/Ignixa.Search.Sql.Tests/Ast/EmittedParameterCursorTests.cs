using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql.Tests.Ast;

/// <summary>
/// PlanExplainer used to number its own @pN parameters with a counter kept in step with the emitters by
/// hand. These cover the cursor that replaced it: explain now quotes the names emission bound, and states
/// the value it expects at each one, so a traversal that drifts fails loudly instead of printing a
/// plausible but wrong name.
/// </summary>
public class EmittedParameterCursorTests
{
    [Fact]
    public void GivenParametersReadInBindOrder_WhenNamed_ThenItReturnsTheNamesEmissionBound()
    {
        var cursor = new EmittedParameterCursor([new EmittedSqlParameter("@p0", "a"), new EmittedSqlParameter("@p1", 7)]);

        cursor.Next("a").ShouldBe("@p0");
        cursor.Next(7).ShouldBe("@p1");
        Should.NotThrow(cursor.RequireFullyConsumed);
    }

    [Fact]
    public void GivenAValueReadOutOfBindOrder_WhenNamed_ThenItFailsNamingBothValues()
    {
        // The drift that actually happened: a row claimed ordinals earlier rows had not yet consumed, so
        // every name after it referred to the wrong bound value -- silently.
        var cursor = new EmittedParameterCursor([new EmittedSqlParameter("@p0", "a"), new EmittedSqlParameter("@p1", 7)]);

        var error = Should.Throw<NotSupportedException>(() => cursor.Next(7));

        error.Message.ShouldContain("@p0");
        error.Message.ShouldContain("different order");
    }

    [Fact]
    public void GivenMoreReadsThanEmissionBound_WhenNamed_ThenItFailsRatherThanInventingAName()
    {
        var cursor = new EmittedParameterCursor([new EmittedSqlParameter("@p0", "a")]);
        cursor.Next("a");

        Should.Throw<NotSupportedException>(() => cursor.Next("b"))
            .Message.ShouldContain("binds only 1");
    }

    [Fact]
    public void GivenFewerReadsThanEmissionBound_WhenChecked_ThenItFailsRatherThanSilentlyOmittingARow()
    {
        var cursor = new EmittedParameterCursor([new EmittedSqlParameter("@p0", "a"), new EmittedSqlParameter("@p1", 7)]);
        cursor.Next("a");

        Should.Throw<NotSupportedException>(cursor.RequireFullyConsumed)
            .Message.ShouldContain("named 1 parameters");
    }
}
