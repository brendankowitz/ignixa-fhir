namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal sealed record ResourceParityClassification(
    string RootCause,
    ParityReachability Reachability,
    bool BlocksEnablement);
