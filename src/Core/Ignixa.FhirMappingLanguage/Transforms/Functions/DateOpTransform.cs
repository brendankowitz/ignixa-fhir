/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// dateOp(value, [operation], [params]) - Performs date operations.
/// </summary>
internal class DateOpTransform : ITransformFunction
{
    public string Name => "dateOp";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("dateOp() requires at least a value argument");

        var value = arguments[0];
        var operation = arguments.Count > 1 ? arguments[1].ToString() : "parse";

#pragma warning disable CA1308 // Normalize strings to uppercase - FHIR operation names are lowercase by convention
        var op = operation?.ToLowerInvariant();
#pragma warning restore CA1308

        // Handle operations
        if (op == "add" || op == "subtract")
        {
            if (arguments.Count < 4)
                throw new ArgumentException($"{op}() requires date, operation, amount, and unit arguments");

            var date = value is DateTime dt ? dt : DateTime.Parse(value.ToString()!);
            var amount = Convert.ToInt32(arguments[2]);
#pragma warning disable CA1308 // Normalize strings to uppercase - FHIR unit names are lowercase by convention
            var unit = arguments[3].ToString()!.ToLowerInvariant();
#pragma warning restore CA1308

            var multiplier = op == "subtract" ? -1 : 1;
            var adjustedAmount = amount * multiplier;

            return unit switch
            {
                "years" => date.AddYears(adjustedAmount),
                "months" => date.AddMonths(adjustedAmount),
                "days" => date.AddDays(adjustedAmount),
                "hours" => date.AddHours(adjustedAmount),
                "minutes" => date.AddMinutes(adjustedAmount),
                "seconds" => date.AddSeconds(adjustedAmount),
                _ => date
            };
        }

        var valueStr = value.ToString()!;
        return op switch
        {
            "parse" => ParseDate(valueStr),
            "now" => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            "today" => DateTime.Today.ToString("yyyy-MM-dd"),
            _ => valueStr
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
