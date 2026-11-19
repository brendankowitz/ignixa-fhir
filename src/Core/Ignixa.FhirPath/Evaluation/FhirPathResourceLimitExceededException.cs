// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Signals that evaluation was stopped because it exceeded a resource limit the engine enforces, such
/// as a regular expression's match timeout, rather than because the expression is ill-formed.
/// </summary>
/// <remarks>
/// <para>
/// It derives from <see cref="FhirPathEvaluationException"/> so that every existing handler keeps seeing
/// it as an evaluation error. Callers that must not treat it like an ill-formed expression catch it first.
/// The distinction matters where an ill-formed expression is excused: <c>FhirPathInvariantCheck</c>
/// downgrades <see cref="FhirPathEvaluationException"/> to a non-failing warning, which for a limit would
/// let data crafted to exhaust the limit pass the invariant. That check fails the resource instead.
/// </para>
/// <para>
/// The data, not only the expression, can trigger it: the same expression may finish on one input and
/// exceed the limit on a larger one.
/// </para>
/// </remarks>
public class FhirPathResourceLimitExceededException : FhirPathEvaluationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FhirPathResourceLimitExceededException"/> class.
    /// </summary>
    public FhirPathResourceLimitExceededException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FhirPathResourceLimitExceededException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public FhirPathResourceLimitExceededException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FhirPathResourceLimitExceededException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public FhirPathResourceLimitExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
