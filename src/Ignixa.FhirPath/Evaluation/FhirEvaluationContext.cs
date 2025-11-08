/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FHIR-specific evaluation context with support for resolve() and terminology services.
 */

using Ignixa.Serialization.Abstractions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// FHIR-specific evaluation context that extends the base EvaluationContext
/// with support for element resolution and terminology services.
/// </summary>
public class FhirEvaluationContext : EvaluationContext
{
    /// <summary>
    /// A function that is invoked when resolve() is called in FhirPath expressions.
    /// Should return the ITypedElement for the given reference (e.g., "Patient/1234").
    /// Should return null if the resource cannot be found.
    /// </summary>
    public Func<string, ITypedElement?>? ElementResolver { get; set; }

    /// <summary>
    /// Terminology service for terminology operations (e.g., memberOf()).
    /// Not yet implemented in the evaluator.
    /// </summary>
    public object? TerminologyService { get; set; }
}
