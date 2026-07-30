/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Shared instance-creation wiring for FHIRPath instance-selector tests.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// The evaluator delegates object construction to the host, so instance-selector tests must supply
/// a creator. This wires the production R4 factory rather than a stub so the tests exercise the
/// real schema-driven behaviour (choice suffixes, cardinality, unknown types).
/// </summary>
internal static class InstanceCreationTestContext
{
    private static readonly R4CoreSchemaProvider Schema = new();
    private static readonly SourceNodeInstanceFactory Factory = new(Schema);

    public static Func<InstanceCreationRequest, IElement?> Creator { get; } = Factory.Create;

    public static EvaluationContext For(IElement focus) =>
        new EvaluationContext()
            .WithFocus(focus)
            .WithInstanceCreator(Creator);
}
