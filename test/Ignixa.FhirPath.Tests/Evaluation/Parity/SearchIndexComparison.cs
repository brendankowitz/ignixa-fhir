namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal sealed record SearchIndexComparison(
    IReadOnlyList<string> FirelyEntries,
    IReadOnlyList<string> IgnixaEntries);
