namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// What one expression corpus is expected to look like as a population, independently of which
/// divergences it produces.
/// </summary>
/// <remarks>
/// <para>
/// The two counts that are exact pins and the two that are floors are chosen for opposite reasons.
/// <see cref="ExpectedBothThrew"/> and <see cref="ExpectedBothEmpty"/> are agreements that establish
/// nothing, so they must not be allowed to grow quietly - an exact pin forces a deliberate decision
/// when they move. <see cref="MinimumEvaluationsPerEngine"/> and
/// <see cref="MinimumAgreementsOnValues"/> are evidence, so they are floors: a floor can only be
/// satisfied by holding or gaining evidence, whereas an exact pin is satisfied by any number that has
/// been written down and makes losing evidence look like routine maintenance.
/// </para>
/// </remarks>
/// <param name="MinimumEvaluationsPerEngine">
/// Lower bound on corpus size times subject count, so a corpus that stopped loading expressions fails
/// instead of trivially satisfying the divergence pins with nothing to compare.
/// </param>
/// <param name="ExpectedBothThrew">
/// Evaluations where both engines threw. Non-zero here by design: <see cref="KnownDivergences"/> pins
/// the <c>hasExtension()</c> parameter at four subjects rather than five because the fifth is a mutual
/// throw, and a mutual throw is agreement to <see cref="ParityOutcome.Matches"/> and so never appears
/// in a divergence list.
/// </param>
/// <param name="ExpectedBothEmpty">Evaluations where both engines returned no results.</param>
/// <param name="MinimumAgreementsOnValues">
/// Lower bound on evaluations where both engines returned the same non-empty results - the only bucket
/// that is positive evidence the two agree.
/// </param>
internal sealed record ExpressionCorpusExpectations(
    int MinimumEvaluationsPerEngine,
    int ExpectedBothThrew,
    int ExpectedBothEmpty,
    int MinimumAgreementsOnValues);
