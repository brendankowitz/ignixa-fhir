/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FHIR-specific evaluation context with support for resolve() and terminology services.
 * Extends the immutable EvaluationContext with FHIR-specific capabilities.
 */

using System.Collections.Immutable;
using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// FHIR-specific evaluation context that extends the base EvaluationContext
/// with support for element resolution and terminology services.
/// </summary>
/// <remarks>
/// This is a sealed record that follows the immutable pattern of EvaluationContext.
/// Use the <c>With*</c> methods to create new instances with modified values.
/// </remarks>
public sealed record FhirEvaluationContext : EvaluationContext
{
    /// <summary>
    /// Creates a new FHIR evaluation context.
    /// </summary>
    public FhirEvaluationContext() : base()
    {
    }

    /// <summary>
    /// Private constructor for creating derived instances.
    /// </summary>
    private FhirEvaluationContext(EvaluationContext baseContext) : base()
    {
    }

    /// <summary>
    /// A function that is invoked when resolve() is called in FhirPath expressions.
    /// Should return the IElement for the given reference (e.g., "Patient/1234").
    /// Should return null if the resource cannot be found.
    /// </summary>
    public Func<string, IElement?>? ElementResolver { get; init; }

    /// <summary>
    /// Terminology service for terminology operations (e.g., memberOf()).
    /// Not yet implemented in the evaluator.
    /// </summary>
    public object? TerminologyService { get; init; }

    /// <summary>
    /// Creates a new context with the specified element resolver.
    /// </summary>
    public FhirEvaluationContext WithElementResolver(Func<string, IElement?> resolver)
    {
        return this with { ElementResolver = resolver };
    }

    /// <summary>
    /// Creates a new context with the specified terminology service.
    /// </summary>
    public FhirEvaluationContext WithTerminologyService(object service)
    {
        return this with { TerminologyService = service };
    }
}
