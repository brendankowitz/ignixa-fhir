// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

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
internal class FhirPathScriptFunctions
{
    private readonly FhirPathParser _parser;
    private readonly FhirPathEvaluator _evaluator;
    private readonly ISchema _schema;
    private readonly ITemplateResolver? _templateResolver;
    private readonly NarrativeTemplateEngine? _templateEngine;

    // Thread-local storage for circular reference tracking
    [ThreadStatic]
    private static Stack<string>? _renderingStack;

    private const int MaxRenderingDepth = 3;

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

        // Public methods will be auto-discovered when this object is imported
        // via scriptObject.Import(this) in NarrativeTemplateEngine
    }

    /// <summary>
    /// Creates a new FhirPathScriptFunctions instance with template rendering support.
    /// </summary>
    /// <param name="schema">The FHIR schema for type information during evaluation.</param>
    /// <param name="templateResolver">The template resolver for nested resource rendering.</param>
    /// <param name="templateEngine">The template engine for nested resource rendering.</param>
    internal FhirPathScriptFunctions(ISchema schema, ITemplateResolver templateResolver, NarrativeTemplateEngine templateEngine)
        : this(schema)
    {
        ArgumentNullException.ThrowIfNull(templateResolver);
        ArgumentNullException.ThrowIfNull(templateEngine);

        _templateResolver = templateResolver;
        _templateEngine = templateEngine;
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
        if (resource is not IElement element || expression is not string exprString || string.IsNullOrEmpty(exprString))
        {
            return string.Empty;
        }

        try
        {
            var parsedExpression = _parser.Parse(exprString);
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
        if (resource is not IElement element || expression is not string exprString || string.IsNullOrEmpty(exprString))
        {
            return [];
        }

        try
        {
            var parsedExpression = _parser.Parse(exprString);
            var results = _evaluator.Evaluate(element, parsedExpression);

            return results.Select(r => r.Value?.ToString() ?? string.Empty);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Evaluates a FHIRPath expression and returns the first result as an IElement.
    /// This allows extracting child elements for nested rendering.
    /// </summary>
    /// <param name="resource">The FHIR resource to evaluate against.</param>
    /// <param name="expression">The FHIRPath expression to evaluate.</param>
    /// <returns>The first result as an IElement, or null if no results.</returns>
    /// <example>
    /// {{ entry_resource = fhir.path_element resource "entry[0].resource" }}
    /// {{ fhir.render_resource entry_resource "Patient" fhirVersion "Html" culture }}
    /// </example>
    public IElement? PathElement(object resource, object expression)
    {
        if (resource is not IElement element || expression is not string exprString || string.IsNullOrEmpty(exprString))
        {
            return null;
        }

        try
        {
            var parsedExpression = _parser.Parse(exprString);
            var results = _evaluator.Evaluate(element, parsedExpression);

            // Return the first result if it's an IElement
            return results.FirstOrDefault();
        }
        catch
        {
            return null;
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
        if (resource is not IElement element || expression is not string exprString || string.IsNullOrEmpty(exprString))
        {
            return false;
        }

        try
        {
            var parsedExpression = _parser.Parse(exprString);
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
    public int Count(object resource, object expression)
    {
        if (resource is not IElement element || expression is not string exprString || string.IsNullOrEmpty(exprString))
        {
            return 0;
        }

        try
        {
            var parsedExpression = _parser.Parse(exprString);
            var results = _evaluator.Evaluate(element, parsedExpression);

            return results.Count();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Renders a nested FHIR resource using the appropriate template for the resource type.
    /// </summary>
    /// <param name="resource">The FHIR resource to render (as IElement).</param>
    /// <param name="resourceType">The resource type (e.g., "Patient", "Observation").</param>
    /// <param name="fhirVersion">The FHIR version of the resource.</param>
    /// <param name="format">The output format (Html, Markdown, Compact).</param>
    /// <param name="culture">The culture for localization.</param>
    /// <returns>The rendered narrative content, or a fallback message if no template is available.</returns>
    /// <remarks>
    /// This method tracks rendering depth to prevent circular references. If a resource is already
    /// being rendered in the call stack (depth > 3), returns a placeholder message.
    /// </remarks>
    /// <example>
    /// {{ fhir.render_resource entry_resource "Patient" fhirVersion "Html" culture }}
    /// </example>
    public string RenderResource(IElement resource, string resourceType, object fhirVersion, string format, string culture)
    {
        // Validate inputs
        if (resource is null || string.IsNullOrEmpty(resourceType))
        {
            return string.Empty;
        }

        // Return fallback message if template resolver or engine not available
        if (_templateResolver is null || _templateEngine is null)
        {
            return $"[{resourceType} - rendering not available]";
        }

        // Initialize rendering stack if needed
        _renderingStack ??= new Stack<string>();

        // Check for circular references or max depth
        if (_renderingStack.Count >= MaxRenderingDepth)
        {
            return $"[{resourceType} - max depth reached]";
        }

        // Create a unique key for this resource to detect circular references
        var resourceId = GetResourceIdentifier(resource, resourceType);
        if (_renderingStack.Contains(resourceId))
        {
            return $"[{resourceType} - circular reference detected]";
        }

        try
        {
            // Push onto stack to track rendering depth
            _renderingStack.Push(resourceId);

            // Parse FHIR version
            var parsedVersion = fhirVersion switch
            {
                FhirVersion v => v,
                string s when Enum.TryParse<FhirVersion>(s, ignoreCase: true, out var v) => v,
                int i when Enum.IsDefined(typeof(FhirVersion), i) => (FhirVersion)i,
                _ => _schema.Version // Fallback to schema version
            };

            // Parse template format
            var parsedFormat = format switch
            {
                "Html" => TemplateFormat.Html,
                "Markdown" or "Md" => TemplateFormat.Markdown,
                "Compact" => TemplateFormat.Compact,
                _ => TemplateFormat.Html // Default to Html
            };

            // Parse culture
            var cultureInfo = CultureInfo.GetCultureInfo(culture);

            // Resolve template (synchronous call - templates are cached)
            var resolution = _templateResolver.ResolveTemplateAsync(
                resourceType,
                parsedVersion,
                parsedFormat,
                CancellationToken.None).GetAwaiter().GetResult();

            if (resolution is null)
            {
                return $"[{resourceType} - no template available]";
            }

            // Render template (synchronous call)
            var rendered = _templateEngine.RenderAsync(
                resolution.Content,
                resource,
                resourceType,
                parsedVersion,
                cultureInfo,
                CancellationToken.None).GetAwaiter().GetResult();

            return rendered;
        }
        catch (Exception)
        {
            // Return fallback on any error to avoid breaking the parent template
            return $"[{resourceType} - rendering error]";
        }
        finally
        {
            // Always pop from stack
            if (_renderingStack.Count > 0)
            {
                _renderingStack.Pop();
            }
        }
    }

    /// <summary>
    /// Gets a unique identifier for a resource to track circular references.
    /// </summary>
    private static string GetResourceIdentifier(IElement resource, string resourceType)
    {
        // Try to extract the resource ID using the IElement interface
        // First, check children for an "id" element (most efficient)
        var idChildren = resource.Children("id");
        if (idChildren.Count > 0)
        {
            var idValue = idChildren[0].Value?.ToString();
            if (!string.IsNullOrEmpty(idValue))
            {
                return $"{resourceType}/{idValue}";
            }
        }

        // Try to access JsonNode metadata for direct JSON access
        var jsonNode = resource.Meta<JsonNode>();
        if (jsonNode is JsonObject jsonObject &&
            jsonObject.TryGetPropertyValue("id", out var idNode) &&
            idNode is JsonValue idJsonValue)
        {
            var id = idJsonValue.GetValue<string>();
            if (!string.IsNullOrEmpty(id))
            {
                return $"{resourceType}/{id}";
            }
        }

        // Fallback to resource type + hash code
        return $"{resourceType}#{resource.GetHashCode()}";
    }

    /// <summary>
    /// Calculates the age from a birth date.
    /// </summary>
    /// <param name="birthDate">The birth date string (e.g., "1990-01-15").</param>
    /// <returns>A formatted age string (e.g., "35 years") or empty string if unable to calculate.</returns>
    /// <example>
    /// {{ fhir.calculate_age (fhir.path resource "birthDate") }}
    /// </example>
    public static string CalculateAge(string? birthDate)
    {
        if (string.IsNullOrEmpty(birthDate))
        {
            return string.Empty;
        }

        // Parse the birth date - handle partial dates
        if (!DateTime.TryParse(birthDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth))
        {
            // Try YYYY-MM format
            if (birthDate.Length == 7 && DateTime.TryParseExact(birthDate, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out birth))
            {
                // Use first day of the month
            }
            else if (birthDate.Length == 4 && int.TryParse(birthDate, out var year))
            {
                // Use January 1st of the year
                birth = new DateTime(year, 1, 1);
            }
            else
            {
                return string.Empty;
            }
        }

        var today = DateTime.Today;
        var age = today.Year - birth.Year;

        // Adjust if birthday hasn't occurred yet this year
        if (birth.Date > today.AddYears(-age))
        {
            age--;
        }

        if (age < 0)
        {
            return string.Empty;
        }

        if (age == 0)
        {
            // Calculate months for infants
            var months = (today.Year - birth.Year) * 12 + today.Month - birth.Month;
            if (today.Day < birth.Day)
            {
                months--;
            }

            if (months <= 0)
            {
                var days = (today - birth).Days;
                return days == 1 ? "1 day" : $"{days} days";
            }

            return months == 1 ? "1 month" : $"{months} months";
        }

        return age == 1 ? "1 year" : $"{age} years";
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

    /// <summary>
    /// Gets structure elements for a resource type.
    /// Returns top-level elements only (depth 1) for template iteration.
    /// </summary>
    public IEnumerable<ElementMetadata> GetStructureElements(string resourceType, object fhirVersionObj)
    {
        if (string.IsNullOrEmpty(resourceType))
        {
            return [];
        }

        try
        {
            var typeDefinition = _schema.GetTypeDefinition(resourceType);
            if (typeDefinition is null)
            {
                return [];
            }

            // Get top-level children elements
            var elements = typeDefinition.Children
                .Where(e => !string.Equals(e.Info.Name, "id", StringComparison.Ordinal))  // Skip 'id' (shown elsewhere)
                .Where(e => !string.Equals(e.Info.Name, "meta", StringComparison.Ordinal))  // Skip 'meta' (shown elsewhere)
                .Where(e => !string.Equals(e.Info.Name, "implicitRules", StringComparison.Ordinal))  // Skip technical fields
                .Where(e => !string.Equals(e.Info.Name, "language", StringComparison.Ordinal))
                .Where(e => !string.Equals(e.Info.Name, "text", StringComparison.Ordinal))  // Skip narrative (we're generating it!)
                .Where(e => !string.Equals(e.Info.Name, "contained", StringComparison.Ordinal))  // Skip contained (complex)
                .Where(e => !string.Equals(e.Info.Name, "extension", StringComparison.Ordinal))  // Skip extensions (for now)
                .Where(e => !string.Equals(e.Info.Name, "modifierExtension", StringComparison.Ordinal))
                .Select(e =>
                {
                    var typeName = e.Info.Name;

                    // If ITypeExtended, use Types collection to get first type
                    string typeCode = "unknown";
                    bool isPrimitive = false;
                    bool isCodeableConcept = false;
                    bool isReference = false;
                    bool isQuantity = false;
                    int min = 0;
                    int max = 1;

                    if (e is ITypeExtended extended)
                    {
                        var firstType = extended.Types.Count > 0 ? extended.Types[0] : null;
                        typeCode = firstType?.Code ?? e.Info.Name;
                        isPrimitive = firstType?.Code is not null &&
                                      _schema.GetTypeDefinition(firstType.Code)?.Info.IsPrimitive == true;
                        isCodeableConcept = extended.Types.Any(t => string.Equals(t.Code, "CodeableConcept", StringComparison.Ordinal));
                        isReference = extended.Types.Any(t => string.Equals(t.Code, "Reference", StringComparison.Ordinal));
                        isQuantity = extended.Types.Any(t => string.Equals(t.Code, "Quantity", StringComparison.Ordinal));
                        min = extended.Min;
                        max = extended.Max == "*" ? int.MaxValue : int.Parse(extended.Max, CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        // Fall back to Info properties
                        typeCode = e.Info.Name;
                        isPrimitive = e.Info.IsPrimitive;
                        min = e.IsRequired ? 1 : 0;
                        max = e.IsCollection ? int.MaxValue : 1;
                    }

                    return new ElementMetadata
                    {
                        Name = typeName,
                        Path = $"{resourceType}.{typeName}",
                        Type = typeCode,
                        IsPrimitive = isPrimitive,
                        IsCodeableConcept = isCodeableConcept,
                        IsReference = isReference,
                        IsQuantity = isQuantity,
                        IsArray = max > 1,
                        Min = min,
                        Max = max,
                        Description = null  // Short description not available in IType interface
                    };
                })
                .ToList();

            return elements;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Formats a value based on its FHIR type.
    /// Handles dates, booleans, and other common types.
    /// </summary>
    public string FormatByType(string? value, string type, CultureInfo? culture = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        try
        {
            return type switch
            {
                "date" => FormatDate(value, culture),
                "dateTime" or "instant" => FormatDateTime(value, culture),
                "boolean" when value == "true" => "Yes",  // Could use localization here
                "boolean" when value == "false" => "No",
                "boolean" => value,
                _ => value
            };
        }
        catch
        {
            return value;
        }
    }

    /// <summary>
    /// Gets the instance type of an IElement, which for FHIR resources is the resourceType.
    /// </summary>
    /// <param name="element">The element to get the instance type from.</param>
    /// <returns>The element instance type (resourceType for resources), or empty string if not available.</returns>
    /// <example>
    /// {{~ entry_resource = fhir.path_element resource ("entry[" + i + "].resource") ~}}
    /// {{~ entry_type = fhir.get_element_name entry_resource ~}}
    /// </example>
    /// <remarks>
    /// This is useful for extracting the resourceType from bundle entry resources,
    /// where FHIRPath queries like "resourceType" don't work on extracted IElement instances.
    /// The IElement.InstanceType property contains the runtime type (e.g., "Patient", "Observation").
    /// </remarks>
    public static string GetElementName(object? element)
    {
        if (element is IElement typedElement)
        {
            return typedElement.InstanceType ?? string.Empty;
        }

        return string.Empty;
    }

    /// <summary>
    /// Gets the display text for a code from a FHIR ValueSet using the schema.
    /// </summary>
    /// <param name="system">The code system URL (e.g., "http://hl7.org/fhir/name-use").</param>
    /// <param name="code">The code value (e.g., "official").</param>
    /// <returns>The display text from the ValueSet, or the code itself if not found.</returns>
    /// <example>
    /// {{ fhir.code_display "http://hl7.org/fhir/name-use" name_use }}
    /// </example>
    /// <remarks>
    /// This method looks up the code in FHIR ValueSets using the schema's ValueSetProvider.
    /// ValueSet URLs follow the pattern: http://hl7.org/fhir/ValueSet/{name} (derived from system URL).
    /// If the code is not found in any ValueSet, it falls back to returning the code itself.
    /// </remarks>
    public string CodeDisplay(string? system, string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(system))
        {
            return code;
        }

        try
        {
            // Cast to IFhirSchemaProvider to access ValueSetProvider
            if (_schema is not IFhirSchemaProvider schemaProvider)
            {
                return code;
            }

            var valueSetProvider = schemaProvider.ValueSetProvider;

            // Try to find a ValueSet for this system
            // FHIR ValueSet URLs typically follow the pattern: http://hl7.org/fhir/ValueSet/{name}
            // where {name} is derived from the code system URL
            var valueSetUrl = system.Replace("/fhir/", "/fhir/ValueSet/", StringComparison.Ordinal);

            var codes = valueSetProvider.GetCodes(valueSetUrl);
            if (codes is not null)
            {
                var matchingCode = codes.FirstOrDefault(c =>
                    string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

                if (matchingCode.Display is not null && !string.IsNullOrEmpty(matchingCode.Display))
                {
                    return matchingCode.Display;
                }
            }

            // Fall back to the code itself
            return code;
        }
        catch
        {
            return code;
        }
    }
}
