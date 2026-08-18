/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The observable result of evaluating one expression on one engine.
 */

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// Everything a caller of <c>Select</c> can observe about one evaluation: the rendered results, and
/// whether the call threw instead of returning.
/// </summary>
/// <remarks>
/// <para>
/// A throw is recorded rather than propagated. "One engine throws where the other returns empty" is
/// the single most important thing this harness exists to find - ADR 2608 names it as the mechanism
/// by which a conformance gap becomes silent search-index drift - so it has to be a comparable value,
/// not a stack trace that aborts the comparison.
/// </para>
/// <para>
/// <see cref="ExceptionType"/> is captured for the inventory but deliberately excluded from
/// <see cref="Matches"/>. Firely and Ignixa have unrelated exception hierarchies, so comparing type
/// names would report every mutually-agreed error as a divergence and bury the real ones.
/// </para>
/// </remarks>
internal sealed record ParityOutcome(bool Threw, string? ExceptionType, IReadOnlyList<string> Results)
{
    public static ParityOutcome Returned(IReadOnlyList<string> results) => new(false, null, results);

    public static ParityOutcome Failed(Exception exception) => new(true, exception.GetType().Name, []);

    /// <summary>
    /// Whether two engines reached the same observable answer: both threw, or both returned the same
    /// rendered results in the same order.
    /// </summary>
    public bool Matches(ParityOutcome other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Threw || other.Threw)
        {
            return Threw && other.Threw;
        }

        return Results.SequenceEqual(other.Results, StringComparer.Ordinal);
    }

    /// <summary>
    /// A short rendering used both in failure messages and in the generated inventory.
    /// </summary>
    public string Describe()
    {
        if (Threw)
        {
            return $"threw {ExceptionType}";
        }

        return Results.Count == 0 ? "empty" : $"[{string.Join(", ", Results)}]";
    }

    /// <summary>
    /// The shape of the outcome with the concrete values removed, so that two divergences caused by the
    /// same underlying behaviour collapse to one signature in the pinned expectations.
    /// </summary>
    public string Shape()
    {
        if (Threw)
        {
            return $"threw:{ExceptionType}";
        }

        return Results.Count == 0 ? "empty" : $"{Results.Count} result(s)";
    }
}
