/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Names a deliberately unimplemented FHIR-specific feature, so a conformance harness can tell one
 * from an engine gap.
 */

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Signals that a named FHIR-specific FHIRPath feature is deliberately not implemented because it
/// depends on infrastructure this engine does not carry - profile validation, a terminology server,
/// or CDA support.
/// </summary>
/// <remarks>
/// <para>
/// The distinction this type exists to draw is between "we chose not to build this" and "we have not
/// built this yet". Both used to surface as a bare <see cref="NotSupportedException"/>, and the
/// official-suite harness caught that type and recorded the case as passed. That made the two
/// indistinguishable to the one consumer whose whole job is telling them apart: nine cases were
/// reported as conformant because a chosen non-feature threw, and an unimplemented binary operator or
/// a corrupt parse tree would have been reported the same way.
/// </para>
/// <para>
/// <see cref="FeatureName"/> is the discriminator, not the message. A harness allowlists the feature
/// names it is willing to record as deliberate, so a name that is not on the list fails even though
/// its exception type is on it. Function features are named without parentheses
/// (<c>conformsTo</c>); environment-variable features keep their sigil (<c>%terminologies</c>).
/// </para>
/// <para>
/// It derives from <see cref="NotSupportedException"/> deliberately: existing catch sites, such as
/// the FHIRPath invariant check in Ignixa.Validation, keep working unchanged, so narrowing the type
/// is not a breaking change. Callers that care about the distinction opt in by catching this type
/// first.
/// </para>
/// <para>
/// This is not the type for a feature that is merely unfinished, nor for an internal invariant
/// violation. An unimplemented operator should stay a bare <see cref="NotSupportedException"/> and a
/// broken invariant an <see cref="InvalidOperationException"/>, so both keep being loud.
/// </para>
/// </remarks>
public sealed class FhirPathFunctionNotSupportedException : NotSupportedException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FhirPathFunctionNotSupportedException"/> class.
    /// </summary>
    /// <param name="featureName">
    /// The function name (<c>conformsTo</c>) or environment-variable name (<c>%terminologies</c>)
    /// that is deliberately not implemented.
    /// </param>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public FhirPathFunctionNotSupportedException(string featureName, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);
        FeatureName = featureName;
    }

    /// <summary>
    /// Gets the name of the feature that is deliberately not implemented.
    /// </summary>
    public string FeatureName { get; }
}
