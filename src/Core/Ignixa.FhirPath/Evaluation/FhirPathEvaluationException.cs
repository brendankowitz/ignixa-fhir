/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The error signal FhirPath itself mandates, kept distinguishable from an engine defect.
 */

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Signals an evaluation error the FHIRPath specification requires: the expression is ill-formed for
/// the data it was handed - a singleton operator given a collection, an operator applied to operand
/// types it is not defined for - and the spec says to signal an error rather than yield a value.
/// </summary>
/// <remarks>
/// <para>
/// The distinction this type exists to draw is between "the expression is wrong" and "we are wrong".
/// Both used to surface as a bare <see cref="InvalidOperationException"/>, which left callers no way
/// to tell a defective constraint from a defect in the engine. Validation is the case that made this
/// concrete: R4's <c>tim-9</c> feeds <c>in</c> a repeating <c>Timing.repeat.when</c>, so the engine
/// correctly refuses - and a resource carrying two <c>when</c> codes was being reported invalid for
/// it. A constraint the engine correctly refuses to evaluate says nothing about the instance.
/// </para>
/// <para>
/// It derives from <see cref="InvalidOperationException"/> deliberately: every existing catch site
/// and every consumer already catching that type keeps working unchanged, so narrowing the type is
/// not a breaking change. Callers that care about the distinction opt in by catching this type
/// first.
/// </para>
/// <para>
/// This is not the type for internal invariant violations - an unreachable switch arm, a missing
/// dependency, a corrupt AST. Those stay <see cref="InvalidOperationException"/> so they keep being
/// loud.
/// </para>
/// </remarks>
public class FhirPathEvaluationException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FhirPathEvaluationException"/> class.
    /// </summary>
    public FhirPathEvaluationException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FhirPathEvaluationException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public FhirPathEvaluationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FhirPathEvaluationException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public FhirPathEvaluationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
