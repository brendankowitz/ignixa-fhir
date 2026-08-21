namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// One resource's canonicalised index entries from both engines, plus every reference-side failure
/// observed while producing the Firely half.
/// </summary>
/// <remarks>
/// <see cref="FirelyFailures"/> is part of the comparison rather than a side channel because entry
/// equality alone cannot distinguish agreement from mutual silence: a Firely expression that throws
/// contributes nothing, and the production Ignixa indexer contains its own evaluation failures the
/// same way. A caller that asserts only on the two entry lists is asserting that two engines failed
/// compatibly, not that they agreed.
/// </remarks>
internal sealed record SearchIndexComparison(
    IReadOnlyList<string> FirelyEntries,
    IReadOnlyList<string> IgnixaEntries,
    IReadOnlyList<ReferenceEvaluationFailure> FirelyFailures);
