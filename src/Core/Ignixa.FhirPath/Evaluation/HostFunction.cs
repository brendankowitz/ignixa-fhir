/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * A function a host binds into FhirPath evaluation.
 */

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// A function the host provides to expressions through <see cref="EvaluationContext.HostFunctions"/>, for operations only the
/// host can perform (a search over its own data, say), in the way <c>resolve()</c> reaches the host through
/// <see cref="FhirEvaluationContext.ElementResolver"/>.
/// </summary>
/// <param name="focus">The function's input collection.</param>
/// <param name="arguments">
/// Each argument evaluated once in the context the call is written in (its <c>$this</c>), not against the focus, as a
/// non-iterating function such as <c>startsWith()</c> evaluates its argument; one collection per argument, in order.
/// </param>
/// <returns>The function's result collection.</returns>
/// <remarks>
/// Failures follow the engine's conventions: throw <see cref="InvalidOperationException"/> or <see cref="ArgumentException"/>
/// for an input or argument the function cannot accept. Exceptions are not wrapped.
/// </remarks>
public delegate IEnumerable<IElement> HostFunction(IReadOnlyList<IElement> focus, IReadOnlyList<IReadOnlyList<IElement>> arguments);
