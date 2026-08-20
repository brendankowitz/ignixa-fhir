namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal sealed record SearchIndexParityReport(
    int ResourceCount,
    TimeSpan Elapsed,
    IReadOnlyList<SearchIndexDivergence> Divergences);
