/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Drives Ignixa's engine through the same public entry point production search indexing uses.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// Evaluates a FHIRPath expression through <c>TypedElementExtensions.Select</c> and renders the
/// outcome into the harness's canonical form.
/// </summary>
internal static class IgnixaEngine
{
    private static readonly R4CoreSchemaProvider Schema = new();

    /// <summary>
    /// Parses JSON into the element model Ignixa evaluates over.
    /// </summary>
    public static IElement Parse(string json) => ResourceJsonNode.Parse(json).ToElement(Schema);

    /// <summary>
    /// Evaluates with <c>%resource</c> and <c>%rootResource</c> bound to the subject and a
    /// <c>resolve()</c> resolver installed, matching what <see cref="FirelyEngine"/> is given.
    /// </summary>
    public static ParityOutcome Evaluate(IElement subject, string expression)
    {
        try
        {
            var results = subject.Select(expression, Context(subject)).ToList();

            return ParityOutcome.Returned(results.ConvertAll(Render));
        }
        catch (Exception exception)
        {
            return ParityOutcome.Failed(exception);
        }
    }

    public static ParityOutcome Evaluate(IElement subject, ISchema schema, string expression)
    {
        try
        {
            var results = subject.Select(expression, Context(subject, schema)).ToList();

            return ParityOutcome.Returned(results.ConvertAll(Render));
        }
        catch (Exception exception)
        {
            return ParityOutcome.Failed(exception);
        }
    }

    /// <summary>
    /// Binds %resource, %rootResource and the resolve() resolver.
    /// </summary>
    /// <remarks>
    /// Firely infers %resource and %rootResource from the ScopedNode its extension methods wrap the
    /// input in. Ignixa's IElement has no parent link, so nothing can infer them and the bridge has to
    /// bind them explicitly - which is exactly what ADR 2608's evaluation-context bridge does. Getting
    /// the same answers from the two engines therefore depends on this binding being present, not on
    /// the engines agreeing on their own.
    /// </remarks>
    private static FhirEvaluationContext Context(IElement subject) =>
        Context(subject, Schema);

    private static FhirEvaluationContext Context(IElement subject, ISchema schema) =>
        new FhirEvaluationContext
        {
            ElementResolver = reference => Resolve(reference, schema),
        } with
        {
            Resource = subject,
            RootResource = subject,
            Schema = schema,
        };

    /// <summary>
    /// Renders one result element as "InstanceType|value".
    /// </summary>
    public static string Render(IElement element) =>
        $"{ParityTypeName.Canonical(element.InstanceType)}|{ParityValue.Render(element.Value, element.InstanceType)}";

    /// <summary>
    /// The un-canonicalised <c>InstanceType</c> of each result.
    /// </summary>
    public static IReadOnlyList<string> RawInstanceTypes(IElement subject, string expression) =>
        subject.Select(expression, Context(subject)).Select(element => element.InstanceType).ToList();

    /// <summary>
    /// The raw values of each result, so a test can pin an exact literal.
    /// </summary>
    public static IReadOnlyList<string> RawValues(IElement subject, string expression) =>
        subject.Select(expression, Context(subject))
            .Select(element => ParityValue.RenderText(element.Value))
            .ToList();

    /// <summary>
    /// Ignixa's own <c>Scalar</c>, for comparison with Firely 5.11.4's throwing one.
    /// </summary>
    public static string ScalarOutcome(IElement subject, string expression)
    {
        try
        {
            return subject.Scalar(expression, Context(subject))?.ToString() ?? "<null>";
        }
        catch (Exception exception)
        {
            return $"threw {exception.GetType().Name}";
        }
    }

    public static bool IsTrue(IElement subject, string expression) =>
        subject.IsTrue(expression, Context(subject));

    private static IElement? Resolve(string reference)
        => Resolve(reference, Schema);

    private static IElement? Resolve(string reference, ISchema schema)
    {
        var json = ParityReferenceResolver.SynthesiseTarget(reference);

        return json is null ? null : ResourceJsonNode.Parse(json).ToElement(schema);
    }
}
