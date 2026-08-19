/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * A named FHIR resource the parity corpus is evaluated against.
 */

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// One subject resource, carried as JSON so that each engine parses it with its own reader and the
/// comparison covers the element model as well as the evaluator.
/// </summary>
internal sealed record ParityResource(string Name, string Json);
