namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal sealed record ResourceParityReport(
    int ResourceCount,
    int SelectEvaluationsPerEngine,
    TimeSpan Elapsed,
    IReadOnlyList<ParityDivergence> Divergences);
