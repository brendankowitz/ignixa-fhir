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
    /// <remarks>
    /// <para>
    /// A double-throw counts as agreement without comparing <see cref="ExceptionType"/>. Firely and
    /// Ignixa have unrelated exception hierarchies - Firely raises SDK types such as
    /// <c>ArgumentException</c> or <c>InvalidOperationException</c> for conditions Ignixa reports as its
    /// own <c>FhirPathEvaluationException</c> - so requiring the same type name would report every
    /// mutually-agreed error as a divergence, whether or not the underlying cause matches. That is a
    /// worse harness, not a stricter one: it would bury the throw-versus-return disagreements this
    /// harness exists to find under noise that only says "the SDKs are different libraries".
    /// </para>
    /// <para>
    /// The consequence is that a double-throw is invisible individually. <see cref="Shape"/> is reached
    /// only through <c>ParityDivergence.Signature</c>, and a double-throw returns <see langword="true"/>
    /// here, so it never becomes a divergence and its <see cref="ExceptionType"/> is never rendered
    /// anywhere. Two engines failing for completely unrelated reasons is therefore indistinguishable
    /// from two engines agreeing on a value. That is contained at the sweep level instead:
    /// <c>ResourceParityReport.BothThrew</c> and <c>BothEmpty</c> count these outcomes and are pinned by
    /// <c>ResourceBackedParityCorpusTests</c>, so the population cannot shift without failing a test even
    /// though no individual double-throw is a divergence.
    /// </para>
    /// </remarks>
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
    /// The shape of the outcome, rendered with its concrete values, so that a pin can only match the
    /// specific divergence it was written for rather than any divergence with the same result count.
    /// </summary>
    /// <remarks>
    /// A count-only signature such as "1 result(s)" is satisfied by any single-result divergence,
    /// including one where the value silently changed to something wrong - a pin written for a benign
    /// scale difference would then keep passing over a genuine regression. Rendering the values closes
    /// that gap, but the signature is also a pinned string committed to <see cref="KnownDivergences"/>,
    /// so it has to be deterministic: results are already culture-invariant strings (see
    /// <c>FirelyEngine.RenderValue</c> / <c>IgnixaEngine.RenderValue</c>), ordering is preserved rather
    /// than sorted, and both a single value and the whole rendering are truncated at a fixed length so
    /// an arbitrarily long result - a big string, a deeply nested collection - cannot make the pin
    /// unbounded or push it into per-machine variance.
    /// </remarks>
    public string Shape()
    {
        if (Threw)
        {
            return $"threw:{ExceptionType}";
        }

        if (Results.Count == 0)
        {
            return "empty";
        }

        var rendered = string.Join(", ", Results.Select(TruncateValue));
        return TruncateShape($"{Results.Count} result(s): [{rendered}]");
    }

    private const int MaxValueLength = 60;
    private const int MaxShapeLength = 320;

    private static string TruncateValue(string value) =>
        value.Length <= MaxValueLength ? value : $"{value[..MaxValueLength]}~(+{value.Length - MaxValueLength})";

    private static string TruncateShape(string shape) =>
        shape.Length <= MaxShapeLength ? shape : $"{shape[..MaxShapeLength]}~(+{shape.Length - MaxShapeLength})";
}
