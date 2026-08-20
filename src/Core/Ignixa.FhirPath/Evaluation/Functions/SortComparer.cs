/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Where a missing sort key goes. The ordering of the keys that are present is ValueOrdering's.
 */

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Adapts <see cref="ValueOrdering.CompareForSort"/> to <see cref="IComparer{T}"/> for <c>sort()</c>.
/// </summary>
/// <remarks>
/// <para>
/// This replaces two near-identical comparers that differed only in where a null key sorted, and that
/// both dispatched on the non-generic <see cref="IComparable"/>. <see cref="FhirTemporal"/> deliberately
/// does not implement it - see the <c>CA1036</c> suppression on that type - and
/// <see cref="FhirQuantity"/> implements no ordering at all, so both fell through to an ordinal compare
/// of <c>ToString()</c>. That sorted <c>10 'mg'</c> before <c>9 'mg'</c> and separated two spellings of
/// the same instant. Both comparers also wrapped <c>CompareTo</c> in a bare <c>catch</c> that answered
/// "equal", so a genuine type mismatch silently interleaved unrelated values.
/// </para>
/// <para>
/// Firely is not a reference for any of this. The version behind the fhir-server seam, 5.11.4 - the one
/// this repo's FHIRPath test project pins by <c>VersionOverride</c> and the one the Firely benchmark
/// builds against - does not implement <c>sort()</c>: a scan of its <c>Hl7.Fhir.Base.dll</c> finds no
/// registration for <c>sort</c>, <c>avg</c>, <c>sum</c> or <c>max</c>, and no <c>ValueProviderComparer</c>,
/// <c>runSort</c> or <c>OrderedNode</c> type. Nothing is claimed here about any other release; an earlier
/// revision of this comment asserted that those members "exist only on Firely's later development line",
/// which was wrong, and it was wrong because it characterised versions nobody had opened.
/// </para>
/// </remarks>
internal sealed class SortComparer : IComparer<IElement?>
{
    private readonly bool _nullsHigh;

    private SortComparer(bool nullsHigh)
    {
        _nullsHigh = nullsHigh;
    }

    /// <summary>
    /// Gets the comparer that treats a missing key as less than any value.
    /// </summary>
    public static SortComparer NullsLow { get; } = new(nullsHigh: false);

    /// <summary>
    /// Gets the comparer that treats a missing key as greater than any value, so that a descending sort
    /// - which negates this comparer's result - places missing keys first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// §sort() says "An empty value is considered lower than all other values, meaning they will appear
    /// before others when sorted ascending", which read alone would put missing keys last in a descending
    /// sort. The official HL7 test suite requires otherwise: <c>testSort10</c> in
    /// <c>r5/fhirpath/tests-fhir-r5.xml</c> is
    /// <c>Patient.name.sort(-family, -given.first()).first().use = 'usual'</c>, and in
    /// <c>patient-example</c> the <c>usual</c> name is the one with no <c>family</c> at all. So a missing
    /// key leads in both directions, and the direction modifier reorders the values rather than the
    /// present-versus-absent partition. This instance exists to make that hold.
    /// </para>
    /// <para>
    /// Neither source is normative, and an earlier revision of this comment said "the normative suite",
    /// which was wrong twice over. §sort() carries the Standard for Trial Use note and every paragraph in
    /// it is tagged <c>{:.stu}</c>; the released Normative 2.0.0 has no <c>sort()</c> at all. And all ten
    /// of <c>testSort1</c>..<c>testSort10</c> are annotated
    /// <c>description="Prototype definition - not part of spec yet"</c>. The <c>testSort10</c> reasoning
    /// above still holds - it is the only evidence either way about the descending case - but it is
    /// evidence from a prototype, not a conformance requirement. Checked 2026-08-19 against the
    /// continuous build off <c>master</c> and <c>FHIR/fhir-test-cases</c>.
    /// </para>
    /// </remarks>
    public static SortComparer NullsHigh { get; } = new(nullsHigh: true);

    /// <summary>
    /// Orders two sort keys.
    /// </summary>
    /// <param name="x">The left key, or <see langword="null"/> when the key expression yielded nothing.</param>
    /// <param name="y">The right key, or <see langword="null"/> when the key expression yielded nothing.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    /// <exception cref="FhirPathEvaluationException">The two keys have no ordering defined between them.</exception>
    public int Compare(IElement? x, IElement? y)
    {
        const string Function = "sort()";

        var leftPresent = HasOrderableValue(x);
        var rightPresent = HasOrderableValue(y);

        if (!leftPresent && !rightPresent)
        {
            return 0;
        }

        if (!leftPresent)
        {
            return _nullsHigh ? 1 : -1;
        }

        if (!rightPresent)
        {
            return _nullsHigh ? -1 : 1;
        }

        return ValueOrdering.CompareForSort(x!, y!, Function);
    }

    /// <summary>
    /// Determines whether a key element carries a value to order on.
    /// </summary>
    /// <remarks>
    /// A resource-backed Quantity carries no <see cref="IElement.Value"/> of its own - value and unit are
    /// children - so a bare null test on the value reported <c>Observation.value</c> as a missing key and
    /// left a collection of quantities in arrival order.
    /// </remarks>
    private static bool HasOrderableValue(IElement? element)
    {
        return element is not null && (element.Value is not null || ValueOrdering.AsQuantity(element) is not null);
    }
}
