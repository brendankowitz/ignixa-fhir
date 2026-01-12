namespace Ignixa.FhirPath.Tests.TestHelpers;

public record FhirPathTestCase(
    string Name,
    string GroupName,
    string Expression,
    string? InputFile,
    IReadOnlyList<ExpectedOutput> ExpectedOutputs,
    bool IsInvalidTest,
    string? InvalidType,
    bool Ordered,
    bool Predicate,
    string? Description,
    string? Mode
);
