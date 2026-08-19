/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Drives Ignixa's engine through the same public entry point production search indexing uses.
 */

using System.Globalization;
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
        new FhirEvaluationContext
        {
            ElementResolver = Resolve,
        } with
        {
            Resource = subject,
            RootResource = subject,
            Schema = Schema,
        };

    /// <summary>
    /// Renders one result element as "InstanceType|value".
    /// </summary>
    public static string Render(IElement element) =>
        $"{ParityTypeName.Canonical(element.InstanceType)}|{RenderValue(element.Value)}";

    /// <summary>
    /// The un-canonicalised <c>InstanceType</c> of each result.
    /// </summary>
    public static IReadOnlyList<string> RawInstanceTypes(IElement subject, string expression) =>
        subject.Select(expression, Context(subject)).Select(element => element.InstanceType).ToList();

    /// <summary>
    /// The raw values of each result, so a test can pin an exact literal.
    /// </summary>
    public static IReadOnlyList<string> RawValues(IElement subject, string expression) =>
        subject.Select(expression, Context(subject)).Select(element => RenderValue(element.Value)).ToList();

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

    /// <summary>
    /// The Ignixa half of the canonical rendering described on
    /// <see cref="FirelyEngine"/>'s equivalent.
    /// </summary>
    private static string RenderValue(object? value) => value switch
    {
        null => "<null>",
        bool flag => flag ? "true" : "false",
        FhirTemporal temporal => temporal.Literal,
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "<null>"
    };

    private static IElement? Resolve(string reference)
    {
        var json = ParityReferenceResolver.SynthesiseTarget(reference);

        return json is null ? null : Parse(json);
    }
}
