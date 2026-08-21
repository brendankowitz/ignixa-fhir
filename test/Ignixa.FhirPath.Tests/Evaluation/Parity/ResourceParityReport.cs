namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// The outcome of one full <c>Select</c> parity sweep.
/// </summary>
/// <remarks>
/// <see cref="BothThrew"/> and <see cref="BothEmpty"/> record the agreements that produce no
/// divergence and are therefore invisible to every per-divergence assertion. Without them the report
/// cannot distinguish a sweep where the engines agreed on real values from one where they agreed by
/// both failing, and a corpus that silently shrank toward zero would still satisfy a divergence count.
/// </remarks>
internal sealed record ResourceParityReport(
    int ResourceCount,
    int SelectEvaluationsPerEngine,
    int BothThrew,
    int BothEmpty,
    TimeSpan Elapsed,
    IReadOnlyList<ParityDivergence> Divergences);
