using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal sealed record SearchIndexDivergence(
    FhirVersion Version,
    string ResourceName,
    IReadOnlyList<string> FirelyEntries,
    IReadOnlyList<string> IgnixaEntries);
