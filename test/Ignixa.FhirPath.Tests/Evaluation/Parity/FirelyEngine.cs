/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Drives Firely 5.11.4 - the engine ADR 2608 names as the seam's replacement target.
 *
 * This file is deliberately the only one in the harness that imports Hl7.FhirPath. Firely and Ignixa
 * both publish an EvaluationContext and a Select() extension, so a file that imported both would
 * either not compile or, worse, silently bind to the wrong one.
 */

using System.Globalization;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Hl7.FhirPath;
using P = Hl7.Fhir.ElementModel.Types;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// Evaluates a FHIRPath expression the way the seam's Firely provider will, and renders the outcome
/// into the harness's canonical form.
/// </summary>
internal static class FirelyEngine
{
    private static readonly IStructureDefinitionSummaryProvider Provider = CreateProvider();

    /// <summary>
    /// Parses JSON into the element model Firely evaluates over.
    /// </summary>
    public static ITypedElement Parse(string json) => FhirJsonNode.Parse(json).ToTypedElement(Provider);

    /// <summary>
    /// Evaluates through <c>IValueProviderFPExtensions.Select</c> - the exact entry point the ~27
    /// production call sites use today, including its internal <c>ToScopedNode()</c> wrap, which is
    /// what makes <c>%resource</c> and <c>%rootResource</c> resolvable and is part of the contract
    /// ADR 2608 pins.
    /// </summary>
    public static ParityOutcome Evaluate(ITypedElement subject, string expression)
    {
        try
        {
            // The parameterless constructor is the one 5.11.4 still endorses: it lets the evaluator
            // infer %resource and %rootResource from the ScopedNode that Select() wraps the input in.
            // The overload taking an ITypedElement is obsolete precisely because it overrides that
            // inference, which would have made this harness measure a configuration nobody ships.
            var context = new FhirEvaluationContext { ElementResolver = Resolve };
            var results = subject.Select(expression, context).ToList();

            return ParityOutcome.Returned(results.ConvertAll(Render));
        }
        catch (Exception exception)
        {
            return ParityOutcome.Failed(exception);
        }
    }

    /// <summary>
    /// Renders one result element as "InstanceType|value".
    /// </summary>
    public static string Render(ITypedElement element) =>
        $"{ParityTypeName.Canonical(element.InstanceType)}|{RenderValue(element.Value)}";

    /// <summary>
    /// The un-canonicalised <c>InstanceType</c> of each result, for the tests that pin the naming
    /// difference <see cref="ParityTypeName"/> deliberately normalises away.
    /// </summary>
    public static IReadOnlyList<string> RawInstanceTypes(ITypedElement subject, string expression) =>
        subject.Select(expression, new FhirEvaluationContext())
            .Select(element => element.InstanceType ?? "<untyped>")
            .ToList();

    /// <summary>
    /// The raw values of each result, so a test can pin an exact literal.
    /// </summary>
    public static IReadOnlyList<string> RawValues(ITypedElement subject, string expression) =>
        subject.Select(expression, new FhirEvaluationContext())
            .Select(element => RenderValue(element.Value))
            .ToList();

    /// <summary>
    /// <c>Scalar</c>'s outcome, which ADR 2608 pins: 5.11.4 calls <c>Single()</c> and therefore throws
    /// on two or more results where SDK 6 returns null.
    /// </summary>
    public static string ScalarOutcome(ITypedElement subject, string expression)
    {
        try
        {
            return subject.Scalar(expression)?.ToString() ?? "<null>";
        }
        catch (Exception exception)
        {
            return $"threw {exception.GetType().Name}";
        }
    }

    /// <summary>
    /// <c>Predicate</c>, which is <c>BooleanEval</c>: empty yields true. The seam reimplements this
    /// because it is <c>internal</c> in the SDK, and Ignixa has no equivalent to compare against.
    /// </summary>
    public static bool Predicate(ITypedElement subject, string expression) =>
        IValueProviderFPExtensions.Predicate(subject, expression);

    public static bool IsTrue(ITypedElement subject, string expression) =>
        IValueProviderFPExtensions.IsTrue(subject, expression);

    /// <summary>
    /// Collapses Firely's value representations onto the same canonical strings
    /// <see cref="IgnixaEngine"/> produces.
    /// </summary>
    /// <remarks>
    /// Firely surfaces primitives as the CQL-ish <c>Hl7.Fhir.ElementModel.Types</c> wrappers where
    /// Ignixa surfaces its own <c>FhirTemporal</c> and plain CLR values. Those are representation
    /// differences, not behavioural ones, and leaving them unnormalised would drown the real findings
    /// in thousands of false positives. Anything genuinely semantic - the count of results, their
    /// declared type, the actual text of a value - survives this normalisation intact.
    /// </remarks>
    private static string RenderValue(object? value) => value switch
    {
        null => "<null>",
        bool flag => flag ? "true" : "false",
        P.Boolean boolean => boolean.Value ? "true" : "false",
        P.Date or P.DateTime or P.Time => value.ToString() ?? "<null>",
        P.Quantity quantity => quantity.ToString(),
        P.Decimal number => number.Value.ToString(CultureInfo.InvariantCulture),
        P.Integer integer => integer.Value.ToString(CultureInfo.InvariantCulture),
        P.Long duration => duration.Value.ToString(CultureInfo.InvariantCulture),
        P.String text => text.Value,
        P.Code code => code.Value ?? "<null>",
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "<null>"
    };

    /// <summary>
    /// Backs <c>resolve()</c>, which appears 76 times across the shipped search parameters and is the
    /// single highest-risk behaviour in the seam's evaluation-context bridge. Both engines get a
    /// resolver with identical semantics so that a divergence here is the engine's, not the fixture's.
    /// </summary>
    private static ITypedElement Resolve(string reference)
    {
        var json = ParityReferenceResolver.SynthesiseTarget(reference);

        // Firely types the resolver as returning a non-nullable ITypedElement but treats null as
        // "unresolvable", which is the case every relative reference to an absent resource hits.
        return json is null ? null! : Parse(json);
    }

    private static IStructureDefinitionSummaryProvider CreateProvider()
    {
        // FhirModule does this globally in fhir-server; ADR 2608 moves it into the provider precisely
        // so an engine constructed outside full server startup can still compile resolve().
        FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();

        return ModelInspector.ForAssembly(typeof(Hl7.Fhir.Model.Patient).Assembly);
    }
}
