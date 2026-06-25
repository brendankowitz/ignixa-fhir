/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Result of evaluating a single SQL on FHIR conformance test case.
 */

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Outcome of a single SQL on FHIR conformance test case: whether it passed and,
/// if not, a human-readable reason. Illegal states (a passing outcome with a reason,
/// or a failing outcome without one) are unrepresentable: construction is forced
/// through the <see cref="Pass"/>, <see cref="Fail"/>, and <see cref="Skipped"/> factories.
/// </summary>
public sealed record SqlOnFhirTestCaseOutcome
{
    public bool Passed { get; }

    public string? Reason { get; }

    private SqlOnFhirTestCaseOutcome(bool passed, string? reason)
    {
        Passed = passed;
        Reason = reason;
    }

    public static SqlOnFhirTestCaseOutcome Pass() => new(true, null);

    public static SqlOnFhirTestCaseOutcome Fail(string reason) =>
        new(false, !string.IsNullOrWhiteSpace(reason)
            ? reason
            : throw new ArgumentException("A failure outcome requires a reason.", nameof(reason)));

    public static SqlOnFhirTestCaseOutcome Skipped() => new(false, "skipped");
}
