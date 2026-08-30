using Ignixa.Search.Indexing;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// What the Firely reference indexer produced for one resource: the entries it built, and every
/// evaluation or conversion that threw on the way.
/// </summary>
/// <remarks>
/// Entries and failures travel together because an entry set read on its own cannot say whether a
/// missing entry means "matched nothing" or "the expression threw". Returning them as one value also
/// keeps the failures scoped to a single <c>Extract</c> call, which matters because
/// <see cref="SearchIndexParityHarness"/> caches one indexer per FHIR version and reuses it across
/// every resource in the sweep.
/// </remarks>
internal sealed record ReferenceIndexProjection(
    IReadOnlyList<SearchIndexEntry> Entries,
    IReadOnlyList<ReferenceEvaluationFailure> Failures);
