namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// One resource's canonicalised index entries from both engines, plus every failure either side
/// contained while producing them.
/// </summary>
/// <remarks>
/// <para>
/// The failure lists are part of the comparison rather than a side channel because entry equality
/// alone cannot distinguish agreement from mutual silence: an expression that throws contributes
/// nothing, which is indistinguishable from one that legitimately matched nothing. A caller that
/// asserts only on the two entry lists is asserting that two engines failed compatibly, not that they
/// agreed.
/// </para>
/// <para>
/// Both sides are carried because both sides contain failures. <see cref="FirelyFailures"/> comes from
/// the reference projection, which catches explicitly; <see cref="IgnixaFailures"/> comes from
/// production <c>ElementSearchIndexer</c>, which catches, logs and continues by design. Recording only
/// the reference half left the more dangerous direction - Ignixa throwing where Firely simply matched
/// nothing - scoring as agreement.
/// </para>
/// <para>
/// <see cref="IgnixaFailures"/> is deliberately not filtered to the expressions both engines compile,
/// unlike <see cref="FirelyFailures"/>. Ignixa is the subject of the comparison rather than the
/// reference, so scoping its failures by what Firely happens to compile would let an Ignixa failure
/// hide behind a Firely limitation - the same double-blindness in a new place.
/// </para>
/// </remarks>
internal sealed record SearchIndexComparison(
    IReadOnlyList<string> FirelyEntries,
    IReadOnlyList<string> IgnixaEntries,
    IReadOnlyList<ReferenceEvaluationFailure> FirelyFailures,
    IReadOnlyList<IgnixaEvaluationFailure> IgnixaFailures);
