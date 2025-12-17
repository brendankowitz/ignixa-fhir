// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Ignixa.NarrativeGenerator.Engine.ScriptFunctions;

/// <summary>
/// Scriban script functions for evaluating FHIRPath expressions within templates.
/// </summary>
/// <remarks>
/// <para>
/// This class exposes FHIRPath evaluation capabilities to Scriban templates through
/// a set of helper functions. Usage in templates:
/// </para>
/// <code>
/// {{ fhir.path resource "name.given.first()" }}
/// {{ fhir.format_date resource.birthDate }}
/// {{ fhir.display resource.code }}
/// </code>
/// </remarks>
public class FhirPathScriptFunctions : ScriptObject
{
    private readonly FhirPathParser _parser;
    private readonly FhirPathEvaluator _evaluator;
    private readonly ISchema _schema;

    /// <summary>
    /// Creates a new FhirPathScriptFunctions instance.
    /// </summary>
    /// <param name="schema">The FHIR schema for type information during evaluation.</param>
    public FhirPathScriptFunctions(ISchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        _parser = new FhirPathParser();
        _evaluator = new FhirPathEvaluator();
        _schema = schema;

        // Register methods as callable members
        // Scriban requires functions to be registered as lambda expressions
        this["path"] = (Func<object, object, string?>)((resource, expression) => Path(resource, expression));
        this["fhirpath"] = (Func<object, object, string?>)((resource, expression) => Path(resource, expression));
        this["path_first"] = (Func<object, object, string?>)((resource, expression) => PathFirst(resource, expression));
        this["path_all"] = (Func<object, object, IEnumerable<string>>)((resource, expression) => PathAll(resource, expression));
        this["format_date"] = (Func<string?, CultureInfo?, string>)((date, culture) => FormatDate(date, culture));
        this["format_datetime"] = (Func<string?, CultureInfo?, string>)((datetime, culture) => FormatDateTime(datetime, culture));
        this["display"] = (Func<JsonNode?, string>)(node => Display(node));
        this["display_coding"] = (Func<JsonNode?, string>)(coding => DisplayCoding(coding));
        this["display_reference"] = (Func<JsonNode?, string>)(reference => DisplayReference(reference));
        this["display_quantity"] = (Func<JsonNode?, string>)(quantity => DisplayQuantity(quantity));
        this["exists"] = (Func<object, object, bool>)((resource, expression) => Exists(resource, expression));
        this["is_empty"] = (Func<JsonNode?, bool>)(node => IsEmpty(node));
        this["count"] = (Func<object, object, int>)((resource, expression) => Count(resource, expression));
        this["safe_html"] = (Func<string?, string>)(text => SafeHtml(text));
    }

    /// <summary>
    /// Evaluates a FHIRPath expression and returns the first result as a string.
    /// </summary>
    /// <param name="resource">The FHIR resource to evaluate against.</param>
    /// <param name="expression">The FHIRPath expression to evaluate.</param>
    /// <returns>The first result as a string, or empty string if no results.</returns>
    /// <example>
    /// {{ fhir.path resource "name.given.first()" }}
    /// </example>
    public string? Path(object resource, object expression)
    {
        if (resource is not ResourceJsonNode resourceNode || expression is not string exprString || string.IsNullOrEmpty(exprString))
        {
            return string.Empty;
        }

        try
        {
            var parsedExpression = _parser.Parse(exprString);
            var element = resourceNode.ToElement(_schema);
            var results = _evaluator.Evaluate(element, parsedExpression);

            var firstResult = results.FirstOrDefault();
            return firstResult?.Value?.ToString() ?? string.Empty;
        }
        catch
        {
            // Return empty on evaluation errors to avoid breaking template rendering
            return string.Empty;
        }
    }

    /// <summary>
    /// Evaluates a FHIRPath expression and returns the first result as a string.
    /// Alias for Path() for backward compatibility.
    /// </summary>
    /// <param name="resource">The FHIR resource to evaluate against.</param>
    /// <param name="expression">The FHIRPath expression to evaluate.</param>
    /// <returns>The first result as a string, or empty string if no results.</returns>
    public string? FhirPath(object resource, object expression)
    {
        return Path(resource, expression);
    }

    /// <summary>
    /// Evaluates a FHIRPath expression and returns the first result as a string.
    /// Alias for Path() for clarity in templates.
    /// </summary>
    /// <param name="resource">The FHIR resource to evaluate against.</param>
    /// <param name="expression">The FHIRPath expression to evaluate.</param>
    /// <returns>The first result as a string, or empty string if no results.</returns>
    public string? PathFirst(object resource, object expression)
    {
        return Path(resource, expression);
    }

    /// <summary>
    /// Evaluates a FHIRPath expression and returns all results as strings.
    /// </summary>
    /// <param name="resource">The FHIR resource to evaluate against.</param>
    /// <param name="expression">The FHIRPath expression to evaluate.</param>
    /// <returns>All results as strings.</returns>
    /// <example>
    /// {{ for name in fhir.path_all resource "name.given" }}
    ///   {{ name }}
    /// {{ end }}
    /// </example>
    public IEnumerable<string> PathAll(object resource, object expression)
    {
        if (resource is not ResourceJsonNode resourceNode || expression is not string exprString || string.IsNullOrEmpty(exprString))
        {
            return [];
        }

        try
        {
            var parsedExpression = _parser.Parse(exprString);
            var element = resourceNode.ToElement(_schema);
            var results = _evaluator.Evaluate(element, parsedExpression);

            return results.Select(r => r.Value?.ToString() ?? string.Empty);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Checks if a FHIRPath expression returns any results.
    /// </summary>
    /// <param name="resource">The FHIR resource to evaluate against.</param>
    /// <param name="expression">The FHIRPath expression to evaluate.</param>
    /// <returns>True if the expression returns at least one result.</returns>
    /// <example>
    /// {{ if fhir.exists resource "name" }}
    ///   Name: {{ fhir.path resource "name.given.first()" }}
    /// {{ end }}
    /// </example>
    public bool Exists(object resource, object expression)
    {
        if (resource is not ResourceJsonNode resourceNode || expression is not string exprString || string.IsNullOrEmpty(exprString))
        {
            return false;
        }

        try
        {
            var parsedExpression = _parser.Parse(exprString);
            var element = resourceNode.ToElement(_schema);
            var results = _evaluator.Evaluate(element, parsedExpression);

            return results.Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Counts the number of results from a FHIRPath expression.
    /// </summary>
    /// <param name="resource">The FHIR resource to evaluate against.</param>
    /// <param name="expression">The FHIRPath expression to evaluate.</param>
    /// <returns>The number of results.</returns>
    public new int Count(object resource, object expression)
    {
        if (resource is not ResourceJsonNode resourceNode || expression is not string exprString || string.IsNullOrEmpty(exprString))
        {
            return 0;
        }

        try
        {
            var parsedExpression = _parser.Parse(exprString);
            var element = resourceNode.ToElement(_schema);
            var results = _evaluator.Evaluate(element, parsedExpression);

            return results.Count();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Formats a FHIR date string into a human-readable format using the specified culture.
    /// </summary>
    /// <param name="fhirDate">The FHIR date string (e.g., "1990-01-15").</param>
    /// <param name="culture">Optional culture for formatting. If null, uses the template's culture context.</param>
    /// <returns>A formatted date string (e.g., "January 15, 1990").</returns>
    /// <remarks>
    /// When called from templates with the culture set in TemplateContext.CurrentCulture,
    /// Scriban will automatically pass the culture to this method.
    /// </remarks>
    /// <example>
    /// Birth Date: {{ fhir.format_date (fhir.path resource "birthDate") }}
    /// </example>
    public string FormatDate(string? fhirDate, CultureInfo? culture = null)
    {
        if (string.IsNullOrEmpty(fhirDate))
        {
            return string.Empty;
        }

        var actualCulture = culture ?? CultureInfo.CurrentCulture;

        // Handle partial dates (FHIR allows YYYY, YYYY-MM, YYYY-MM-DD)
        if (DateTime.TryParse(fhirDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date.ToString("MMMM d, yyyy", actualCulture);
        }

        // If only year-month (YYYY-MM)
        if (fhirDate.Length == 7 && DateTime.TryParseExact(fhirDate, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var partialDate))
        {
            return partialDate.ToString("MMMM yyyy", actualCulture);
        }

        // If only year (YYYY)
        if (fhirDate.Length == 4 && int.TryParse(fhirDate, out _))
        {
            return fhirDate;
        }

        // Return as-is if format is not recognized
        return fhirDate;
    }

    /// <summary>
    /// Formats a FHIR dateTime or instant string into a human-readable format using the specified culture.
    /// </summary>
    /// <param name="fhirDateTime">The FHIR dateTime string.</param>
    /// <param name="culture">Optional culture for formatting. If null, uses the template's culture context.</param>
    /// <returns>A formatted dateTime string.</returns>
    /// <remarks>
    /// When called from templates with the culture set in TemplateContext.CurrentCulture,
    /// Scriban will automatically pass the culture to this method.
    /// </remarks>
    public string FormatDateTime(string? fhirDateTime, CultureInfo? culture = null)
    {
        if (string.IsNullOrEmpty(fhirDateTime))
        {
            return string.Empty;
        }

        var actualCulture = culture ?? CultureInfo.CurrentCulture;

        if (DateTimeOffset.TryParse(fhirDateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
        {
            return dateTime.ToString("MMMM d, yyyy 'at' h:mm tt", actualCulture);
        }

        return fhirDateTime;
    }

    /// <summary>
    /// Extracts the display text from a CodeableConcept or Coding.
    /// </summary>
    /// <param name="codeableConceptOrCoding">The CodeableConcept or Coding JSON node.</param>
    /// <returns>The display text, code, or "Unknown" if not found.</returns>
    /// <example>
    /// Code: {{ fhir.display resource.code }}
    /// </example>
    public static string Display(JsonNode? codeableConceptOrCoding)
    {
        if (codeableConceptOrCoding is null)
        {
            return "Unknown";
        }

        if (codeableConceptOrCoding is JsonObject jsonObject)
        {
            // Try CodeableConcept structure: { text, coding: [{ display, code }] }
            if (jsonObject.TryGetPropertyValue("text", out var textNode) && textNode is JsonValue textValue)
            {
                var text = textValue.GetValue<string>();
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }

            // Try coding array
            if (jsonObject.TryGetPropertyValue("coding", out var codingNode) && codingNode is JsonArray codingArray)
            {
                foreach (var coding in codingArray.OfType<JsonObject>())
                {
                    if (coding.TryGetPropertyValue("display", out var displayNode) && displayNode is JsonValue displayValue)
                    {
                        var display = displayValue.GetValue<string>();
                        if (!string.IsNullOrEmpty(display))
                        {
                            return display;
                        }
                    }

                    if (coding.TryGetPropertyValue("code", out var codeNode) && codeNode is JsonValue codeValue)
                    {
                        var code = codeValue.GetValue<string>();
                        if (!string.IsNullOrEmpty(code))
                        {
                            return code;
                        }
                    }
                }
            }

            // Try direct Coding structure: { display, code }
            if (jsonObject.TryGetPropertyValue("display", out var directDisplayNode) && directDisplayNode is JsonValue directDisplayValue)
            {
                var display = directDisplayValue.GetValue<string>();
                if (!string.IsNullOrEmpty(display))
                {
                    return display;
                }
            }

            if (jsonObject.TryGetPropertyValue("code", out var directCodeNode) && directCodeNode is JsonValue directCodeValue)
            {
                var code = directCodeValue.GetValue<string>();
                if (!string.IsNullOrEmpty(code))
                {
                    return code;
                }
            }
        }

        return "Unknown";
    }

    /// <summary>
    /// Extracts the display text from a Coding element.
    /// </summary>
    /// <param name="coding">The Coding JSON node.</param>
    /// <returns>The display text or code.</returns>
    public static string DisplayCoding(JsonNode? coding)
    {
        if (coding is not JsonObject jsonObject)
        {
            return "Unknown";
        }

        if (jsonObject.TryGetPropertyValue("display", out var displayNode) && displayNode is JsonValue displayValue)
        {
            var display = displayValue.GetValue<string>();
            if (!string.IsNullOrEmpty(display))
            {
                return display;
            }
        }

        if (jsonObject.TryGetPropertyValue("code", out var codeNode) && codeNode is JsonValue codeValue)
        {
            var code = codeValue.GetValue<string>();
            if (!string.IsNullOrEmpty(code))
            {
                return code;
            }
        }

        return "Unknown";
    }

    /// <summary>
    /// Extracts the display text from a Reference element.
    /// </summary>
    /// <param name="reference">The Reference JSON node.</param>
    /// <returns>The display text or reference string.</returns>
    /// <example>
    /// Subject: {{ fhir.display_reference resource.subject }}
    /// </example>
    public static string DisplayReference(JsonNode? reference)
    {
        if (reference is not JsonObject jsonObject)
        {
            return "Unknown";
        }

        // Try display first
        if (jsonObject.TryGetPropertyValue("display", out var displayNode) && displayNode is JsonValue displayValue)
        {
            var display = displayValue.GetValue<string>();
            if (!string.IsNullOrEmpty(display))
            {
                return display;
            }
        }

        // Fall back to reference URL
        if (jsonObject.TryGetPropertyValue("reference", out var refNode) && refNode is JsonValue refValue)
        {
            var refString = refValue.GetValue<string>();
            if (!string.IsNullOrEmpty(refString))
            {
                return refString;
            }
        }

        return "Unknown";
    }

    /// <summary>
    /// Formats a Quantity element for display.
    /// </summary>
    /// <param name="quantity">The Quantity JSON node.</param>
    /// <returns>A formatted quantity string (e.g., "5.5 mg").</returns>
    /// <example>
    /// Value: {{ fhir.display_quantity resource.valueQuantity }}
    /// </example>
    public static string DisplayQuantity(JsonNode? quantity)
    {
        if (quantity is not JsonObject jsonObject)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        if (jsonObject.TryGetPropertyValue("value", out var valueNode))
        {
            if (valueNode is JsonValue jsonValue)
            {
                // Try to get as decimal first for precision, fall back to string
                var valueStr = jsonValue.ToString();
                if (!string.IsNullOrEmpty(valueStr))
                {
                    parts.Add(valueStr);
                }
            }
        }

        // Try unit first, then code
        string? unit = null;
        if (jsonObject.TryGetPropertyValue("unit", out var unitNode) && unitNode is JsonValue unitValue)
        {
            unit = unitValue.GetValue<string>();
        }
        else if (jsonObject.TryGetPropertyValue("code", out var codeNode) && codeNode is JsonValue codeValue)
        {
            unit = codeValue.GetValue<string>();
        }

        if (!string.IsNullOrEmpty(unit))
        {
            parts.Add(unit);
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Checks if a JSON node is empty or null.
    /// </summary>
    /// <param name="node">The JSON node to check.</param>
    /// <returns>True if the node is null, empty array, or empty object.</returns>
    public static bool IsEmpty(JsonNode? node)
    {
        if (node is null)
        {
            return true;
        }

        if (node is JsonArray array)
        {
            return array.Count == 0;
        }

        if (node is JsonValue value)
        {
            var str = value.ToString();
            return string.IsNullOrEmpty(str);
        }

        return false;
    }

    /// <summary>
    /// Escapes HTML special characters for safe display.
    /// </summary>
    /// <param name="text">The text to escape.</param>
    /// <returns>HTML-escaped text.</returns>
    public static string SafeHtml(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return System.Web.HttpUtility.HtmlEncode(text);
    }
}
