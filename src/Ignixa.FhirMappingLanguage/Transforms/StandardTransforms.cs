/*
 * Copyright (c) 2025, Sparky Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

using Ignixa.Serialization.Abstractions;
using System.Text.Json.Nodes;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// Registry of standard transform functions.
/// </summary>
public static class StandardTransforms
{
    private static readonly Dictionary<string, ITransformFunction> _functions = new(StringComparer.OrdinalIgnoreCase);

    static StandardTransforms()
    {
        // Core functions
        Register(new CreateTransform());
        Register(new CopyTransform());
        Register(new UuidTransform());

        // String functions
        Register(new TruncateTransform());
        Register(new EscapeTransform());
        Register(new AppendTransform());

        // Type conversion functions
        Register(new CastTransform());
        Register(new EvaluateTransform());

        // FHIR-specific functions
        Register(new CodeableConceptTransform());
        Register(new CodingTransform());
        Register(new QuantityTransform());
        Register(new IdentifierTransform());
        Register(new ContactPointTransform());
        Register(new ReferenceTransform());

        // Terminology functions
        Register(new TranslateTransform());

        // Utility functions
        Register(new PointerTransform());
        Register(new DateOpTransform());
    }

    /// <summary>
    /// Registers a transform function.
    /// </summary>
    public static void Register(ITransformFunction function)
    {
        _functions[function.Name] = function;
    }

    /// <summary>
    /// Gets a transform function by name.
    /// </summary>
    public static ITransformFunction? Get(string name)
    {
        return _functions.TryGetValue(name, out var function) ? function : null;
    }

    /// <summary>
    /// Gets all registered transform functions.
    /// </summary>
    public static IEnumerable<ITransformFunction> All()
    {
        return _functions.Values;
    }
}

#region Core Functions

/// <summary>
/// create(type) - Creates a new FHIR resource or element of the specified type.
/// </summary>
public class CreateTransform : ITransformFunction
{
    public string Name => "create";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("create() requires a type argument");

        var typeName = arguments[0].ToString()!;

        if (context.ResourceCreator == null)
            throw new InvalidOperationException("ResourceCreator not configured in context");

        return context.ResourceCreator(typeName);
    }
}

/// <summary>
/// copy(source) - Copies the source value to the target.
/// </summary>
public class CopyTransform : ITransformFunction
{
    public string Name => "copy";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("copy() requires a source argument");

        // Direct copy of the value
        return arguments[0];
    }
}

/// <summary>
/// uuid() - Generates a new UUID.
/// </summary>
public class UuidTransform : ITransformFunction
{
    public string Name => "uuid";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        return Guid.NewGuid().ToString();
    }
}

#endregion

#region String Functions

/// <summary>
/// truncate(source, length) - Truncates a string to the specified length.
/// </summary>
public class TruncateTransform : ITransformFunction
{
    public string Name => "truncate";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("truncate() requires source and length arguments");

        var source = arguments[0].ToString() ?? string.Empty;
        var length = Convert.ToInt32(arguments[1]);

        return source.Length <= length ? source : source.Substring(0, length);
    }
}

/// <summary>
/// escape(source, format) - Escapes a string for the specified format (url, json, xml).
/// </summary>
public class EscapeTransform : ITransformFunction
{
    public string Name => "escape";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("escape() requires source and format arguments");

        var source = arguments[0].ToString() ?? string.Empty;
        var format = arguments[1].ToString()?.ToLowerInvariant() ?? "url";

        return format switch
        {
            "url" => Uri.EscapeDataString(source),
            "json" => System.Text.Json.JsonSerializer.Serialize(source).Trim('"'),
            "xml" => System.Security.SecurityElement.Escape(source) ?? source,
            _ => throw new ArgumentException($"Unknown escape format: {format}")
        };
    }
}

/// <summary>
/// append(source) - Appends source to a collection.
/// </summary>
public class AppendTransform : ITransformFunction
{
    public string Name => "append";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("append() requires a source argument");

        // For now, just return the source - actual collection handling needs more context
        return arguments[0];
    }
}

#endregion

#region Type Conversion Functions

/// <summary>
/// cast(source, type) - Casts source to the specified type.
/// </summary>
public class CastTransform : ITransformFunction
{
    public string Name => "cast";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("cast() requires source and type arguments");

        var source = arguments[0];
        var targetType = arguments[1].ToString()!.ToLowerInvariant();

        return targetType switch
        {
            "string" => source.ToString() ?? string.Empty,
            "integer" => Convert.ToInt32(source),
            "decimal" => Convert.ToDecimal(source),
            "boolean" => Convert.ToBoolean(source),
            _ => source // Pass through for complex types
        };
    }
}

/// <summary>
/// evaluate(source, path) - Evaluates a FHIRPath expression against the source.
/// </summary>
public class EvaluateTransform : ITransformFunction
{
    public string Name => "evaluate";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("evaluate() requires source and path arguments");

        if (context.FhirPathEvaluator == null)
            throw new InvalidOperationException("FhirPathEvaluator not configured in context");

        var source = arguments[0];
        var path = arguments[1].ToString()!;

        // Source should be an ITypedElement
        if (source is not ITypedElement element)
            throw new ArgumentException("evaluate() requires source to be an ITypedElement");

        var results = context.FhirPathEvaluator(path, element);
        return results.FirstOrDefault()?.Value ?? string.Empty;
    }
}

#endregion

#region FHIR-Specific Functions

/// <summary>
/// cc(system, code, [display]) - Creates a CodeableConcept.
/// </summary>
public class CodeableConceptTransform : ITransformFunction
{
    public string Name => "cc";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("cc() requires system and code arguments");

        var system = arguments[0].ToString()!;
        var code = arguments[1].ToString()!;
        var display = arguments.Count > 2 ? arguments[2].ToString() : null;

        // Create a simple CodeableConcept structure
        var codeableConcept = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = system,
                    ["code"] = code,
                    ["display"] = display
                }
            }
        };

        return codeableConcept;
    }
}

/// <summary>
/// c(system, code, [display]) - Creates a Coding.
/// </summary>
public class CodingTransform : ITransformFunction
{
    public string Name => "c";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("c() requires system and code arguments");

        var system = arguments[0].ToString()!;
        var code = arguments[1].ToString()!;
        var display = arguments.Count > 2 ? arguments[2].ToString() : null;

        var coding = new JsonObject
        {
            ["system"] = system,
            ["code"] = code
        };

        if (display != null)
        {
            coding["display"] = display;
        }

        return coding;
    }
}

/// <summary>
/// qty(value, unit, [system]) - Creates a Quantity.
/// </summary>
public class QuantityTransform : ITransformFunction
{
    public string Name => "qty";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("qty() requires value and unit arguments");

        var value = Convert.ToDecimal(arguments[0]);
        var unit = arguments[1].ToString()!;
        var system = arguments.Count > 2 ? arguments[2].ToString() : "http://unitsofmeasure.org";

        var quantity = new JsonObject
        {
            ["value"] = value,
            ["unit"] = unit,
            ["system"] = system,
            ["code"] = unit
        };

        return quantity;
    }
}

/// <summary>
/// id(value, [system]) - Creates an Identifier.
/// </summary>
public class IdentifierTransform : ITransformFunction
{
    public string Name => "id";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("id() requires a value argument");

        var value = arguments[0].ToString()!;
        var system = arguments.Count > 1 ? arguments[1].ToString() : null;

        var identifier = new JsonObject
        {
            ["value"] = value
        };

        if (system != null)
        {
            identifier["system"] = system;
        }

        return identifier;
    }
}

/// <summary>
/// cp(system, value, [use]) - Creates a ContactPoint.
/// </summary>
public class ContactPointTransform : ITransformFunction
{
    public string Name => "cp";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("cp() requires system and value arguments");

        var system = arguments[0].ToString()!;
        var value = arguments[1].ToString()!;
        var use = arguments.Count > 2 ? arguments[2].ToString() : null;

        var contactPoint = new JsonObject
        {
            ["system"] = system,
            ["value"] = value
        };

        if (use != null)
        {
            contactPoint["use"] = use;
        }

        return contactPoint;
    }
}

/// <summary>
/// reference(source) - Creates a Reference to the source.
/// </summary>
public class ReferenceTransform : ITransformFunction
{
    public string Name => "reference";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("reference() requires a source argument");

        var source = arguments[0];

        // If source is an ITypedElement with an id, create a reference
        if (source is ITypedElement element)
        {
            var resourceType = element.InstanceType;
            var id = element.Children("id").FirstOrDefault()?.Value?.ToString();

            if (resourceType != null && id != null)
            {
                var reference = new JsonObject
                {
                    ["reference"] = $"{resourceType}/{id}"
                };
                return reference;
            }
        }

        // Otherwise, treat as a reference string
        var referenceString = source.ToString()!;
        return new JsonObject
        {
            ["reference"] = referenceString
        };
    }
}

#endregion

#region Terminology Functions

/// <summary>
/// translate(source, map_uri, output) - Translates a code using a ConceptMap.
/// </summary>
public class TranslateTransform : ITransformFunction
{
    public string Name => "translate";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 3)
            throw new ArgumentException("translate() requires source, map_uri, and output arguments");

        if (context.ConceptMapResolver == null)
            throw new InvalidOperationException("ConceptMapResolver not configured in context");

        var sourceCode = arguments[0].ToString()!;
        var mapUri = arguments[1].ToString()!;
        var output = arguments[2].ToString()!;

        var translated = context.ConceptMapResolver(mapUri, sourceCode, output);

        return translated ?? sourceCode; // Return original if translation fails
    }
}

#endregion

#region Utility Functions

/// <summary>
/// pointer(source) - Returns a JSON Pointer to the source element.
/// </summary>
public class PointerTransform : ITransformFunction
{
    public string Name => "pointer";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("pointer() requires a source argument");

        var source = arguments[0];

        if (source is ITypedElement element)
        {
            return element.Location ?? "/";
        }

        return "/";
    }
}

/// <summary>
/// dateOp(value, [operation], [params]) - Performs date operations.
/// </summary>
public class DateOpTransform : ITransformFunction
{
    public string Name => "dateOp";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("dateOp() requires at least a value argument");

        var value = arguments[0].ToString()!;
        var operation = arguments.Count > 1 ? arguments[1].ToString() : "parse";

        return operation?.ToLowerInvariant() switch
        {
            "parse" => ParseDate(value),
            "now" => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            "today" => DateTime.Today.ToString("yyyy-MM-dd"),
            _ => value
        };
    }

    private static string ParseDate(string value)
    {
        if (DateTime.TryParse(value, out var date))
        {
            return date.ToString("yyyy-MM-dd");
        }
        return value;
    }
}

#endregion
