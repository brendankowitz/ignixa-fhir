using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Hands out the <c>@pN</c> names emission actually bound, in bind order, so an explain never computes an
/// ordinal of its own.
/// </summary>
/// <remarks>
/// <see cref="PlanExplainer"/> renders the same parameters the emitted SQL binds and must name them
/// identically. It used to do that by keeping its own counter and incrementing it in an order maintained by
/// hand against the emitters — a duplicated traversal held together by comments, which had already drifted
/// once (an include boundary claimed ordinals that the surrogate range and search-parameter hash had not yet
/// consumed, so every row after it named the wrong value).
///
/// The counter is now a cursor over the real bind list, and every read states the value it expects to be
/// naming. Divergence therefore fails immediately, at the first row that disagrees, with both values in the
/// message — instead of silently printing a plausible but wrong <c>@pN</c>.
/// </remarks>
internal sealed class EmittedParameterCursor(IReadOnlyList<EmittedSqlParameter> bound)
{
    private int _next;

    /// <summary>
    /// The name of the next bound parameter, which must be carrying <paramref name="expected"/>.
    /// </summary>
    internal string Next(object expected)
    {
        if (_next >= bound.Count)
        {
            throw new NotSupportedException(
                $"Explain asked for parameter {_next} but the emitted SQL binds only {bound.Count}. The " +
                "explain traversal renders a parameter the emitters do not, so the two have diverged.");
        }

        var parameter = bound[_next++];
        if (!Equals(parameter.Value, expected))
        {
            throw new NotSupportedException(
                $"Explain named {parameter.Name} expecting '{expected}', but the emitted SQL binds " +
                $"'{parameter.Value}' there. The explain traversal visits parameters in a different order " +
                "from the emitters, so every name from this point on would be wrong.");
        }

        return parameter.Name;
    }

    /// <summary>Confirms the explain consumed exactly the parameters emission bound, once a plan is fully rendered.</summary>
    internal void RequireFullyConsumed()
    {
        if (_next != bound.Count)
        {
            throw new NotSupportedException(
                $"Explain named {_next} parameters but the emitted SQL binds {bound.Count}. The emitters bind " +
                "a parameter the explain traversal does not render, so the two have diverged.");
        }
    }
}
